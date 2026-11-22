using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    // Wire schema of the signed data channel. Property names are lowercase on
    // purpose: they are the JSON contract shared with tools/build-data.ps1.
    internal sealed class DataDocument
    {
        public int schema { get; set; }
        public long seq { get; set; }
        public string generated_utc { get; set; }
        public string valid_until { get; set; }
        public DataTelegram telegram { get; set; }
        public string[] doh { get; set; }
        public DataPayload payload { get; set; }
    }

    internal sealed class DataTelegram
    {
        public string web_host { get; set; }
        public string[] fronts { get; set; }
        public string front_host { get; set; }
        public DataDc[] dc { get; set; }
        public DataDirect[] direct_ws { get; set; }
    }

    internal sealed class DataDc
    {
        public int id { get; set; }
        public string ip { get; set; }
    }

    internal sealed class DataDirect
    {
        public int dc { get; set; }
        public string ip { get; set; }
        public string sni { get; set; }
    }

    internal sealed class DataPayload
    {
        public string version { get; set; }
        public string url { get; set; }
        public string sha256 { get; set; }
    }

    internal static class DataDocumentParser
    {
        internal static DataDocument Parse(string json, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "пустой ответ";
                return null;
            }
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 262144;
                DataDocument document = serializer.Deserialize<DataDocument>(json);
                if (document == null)
                {
                    error = "JSON не содержит объект";
                    return null;
                }
                return document;
            }
            catch (Exception ex)
            {
                error = "JSON: " + ex.Message;
                return null;
            }
        }
    }

    internal static class DataValidation
    {
        private const int MinFronts = 3;
        private const int MaxFronts = 64;

        private static readonly Regex DomainPattern = new Regex(
            @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Sha256Pattern = new Regex(
            @"^[0-9a-fA-F]{64}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static bool Validate(
            DataDocument document, DateTime utcNow, out string error)
        {
            error = null;
            if (document == null)
            {
                error = "нет данных";
                return false;
            }
            if (document.schema != 1)
            {
                error = "схема " + document.schema + " не поддерживается";
                return false;
            }
            if (document.seq <= 0)
            {
                error = "seq должен быть положительным";
                return false;
            }
            DateTime generated;
            DateTime validUntil;
            if (!TryParseUtc(document.generated_utc, out generated))
            {
                error = "generated_utc некорректен";
                return false;
            }
            if (!TryParseUtc(document.valid_until, out validUntil))
            {
                error = "valid_until некорректен";
                return false;
            }
            if (validUntil <= utcNow)
            {
                error = "данные просрочены";
                return false;
            }
            if (generated > utcNow.AddDays(2))
            {
                error = "generated_utc в будущем";
                return false;
            }
            if (document.telegram == null)
            {
                error = "нет блока telegram";
                return false;
            }
            if (!IsDomain(document.telegram.web_host))
            {
                error = "telegram.web_host некорректен";
                return false;
            }
            if (string.IsNullOrEmpty(document.telegram.front_host) ||
                document.telegram.front_host.IndexOf("{dc}", StringComparison.Ordinal) < 0 ||
                document.telegram.front_host.IndexOf("{front}", StringComparison.Ordinal) < 0)
            {
                error = "telegram.front_host некорректен";
                return false;
            }
            string[] fronts = document.telegram.fronts;
            if (fronts == null || fronts.Length < MinFronts || fronts.Length > MaxFronts)
            {
                error = "фронтов должно быть от " + MinFronts + " до " + MaxFronts;
                return false;
            }
            HashSet<string> seenFronts = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < fronts.Length; i++)
            {
                string front = fronts[i];
                if (!IsDomain(front) ||
                    !front.EndsWith(".co.uk", StringComparison.Ordinal) ||
                    front.StartsWith("kws", StringComparison.Ordinal))
                {
                    error = "фронт некорректен: " + (front ?? "null");
                    return false;
                }
                if (!seenFronts.Add(front))
                {
                    error = "повтор фронта: " + front;
                    return false;
                }
            }
            if (document.telegram.dc == null || document.telegram.dc.Length != 5)
            {
                error = "нужно ровно 5 адресов дата-центров";
                return false;
            }
            HashSet<int> seenDc = new HashSet<int>();
            for (int i = 0; i < document.telegram.dc.Length; i++)
            {
                DataDc dc = document.telegram.dc[i];
                if (dc == null || dc.id < 1 || dc.id > 5 || !seenDc.Add(dc.id))
                {
                    error = "id дата-центра некорректен";
                    return false;
                }
                if (!IsAddress(dc.ip))
                {
                    error = "IP дата-центра " + dc.id + " некорректен";
                    return false;
                }
            }
            if (document.telegram.direct_ws == null ||
                document.telegram.direct_ws.Length == 0)
            {
                error = "нет прямых WebSocket-адресов";
                return false;
            }
            for (int i = 0; i < document.telegram.direct_ws.Length; i++)
            {
                DataDirect direct = document.telegram.direct_ws[i];
                if (direct == null || direct.dc < 1 || direct.dc > 5 ||
                    !IsAddress(direct.ip) || !IsDomain(direct.sni))
                {
                    error = "прямой WebSocket-адрес некорректен";
                    return false;
                }
            }
            if (document.doh == null || document.doh.Length == 0)
            {
                error = "нет DoH-эндпоинтов";
                return false;
            }
            for (int i = 0; i < document.doh.Length; i++)
            {
                if (!IsHttps(document.doh[i]))
                {
                    error = "DoH-эндпоинт некорректен: " + (document.doh[i] ?? "null");
                    return false;
                }
            }
            if (document.payload == null)
            {
                error = "нет блока payload";
                return false;
            }
            if (string.IsNullOrEmpty(document.payload.version) ||
                document.payload.version.Length > 40)
            {
                error = "версия payload некорректна";
                return false;
            }
            if (!IsHttps(document.payload.url))
            {
                error = "URL payload некорректен";
                return false;
            }
            if (document.payload.sha256 == null ||
                !Sha256Pattern.IsMatch(document.payload.sha256))
            {
                error = "SHA256 payload некорректен";
                return false;
            }
            return true;
        }

        internal static bool SequenceAllowed(long incoming, long stored)
        {
            return incoming >= stored;
        }

        internal static bool IsDomain(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                value.Length <= 253 &&
                DomainPattern.IsMatch(value);
        }

        internal static bool IsAddress(string value)
        {
            IPAddress address;
            return !string.IsNullOrEmpty(value) &&
                IPAddress.TryParse(value, out address);
        }

        internal static bool IsHttps(string value)
        {
            Uri uri;
            return !string.IsNullOrEmpty(value) &&
                Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(uri.Host);
        }

        internal static bool TryParseUtc(string text, out DateTime value)
        {
            value = DateTime.MinValue;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            return DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out value);
        }
    }

    // Compiled-in last resort: first launch without network, or a cache that
    // failed validation. It is code, not downloaded content, so it carries no
    // signature and never expires.
    internal static class DataSnapshot
    {
        internal const string Json =
            "{\"schema\":1,\"seq\":0," +
            "\"generated_utc\":\"2026-11-22T00:00:00Z\"," +
            "\"valid_until\":\"2099-12-31T23:59:59Z\"," +
            "\"telegram\":{" +
            "\"web_host\":\"web.telegram.org\"," +
            "\"front_host\":\"kws{dc}.{front}\"," +
            "\"fronts\":[" +
            "\"pclead.co.uk\",\"offshor.co.uk\",\"cakeisalie.co.uk\"," +
            "\"noskomnadzor.co.uk\",\"lovetrue.co.uk\",\"sorokdva.co.uk\"," +
            "\"pyatdesyatdva.co.uk\",\"kartoshka.co.uk\",\"sorokodin.co.uk\"," +
            "\"pyatdesyatodin.co.uk\",\"notelega.co.uk\",\"ebally.co.uk\"," +
            "\"nebally.co.uk\",\"havegreatday.co.uk\",\"pomogite.co.uk\"," +
            "\"fixtelega.co.uk\",\"sadnews.co.uk\",\"onedaychamp.co.uk\"," +
            "\"stopblocking.co.uk\",\"nothingthere.co.uk\"]," +
            "\"dc\":[" +
            "{\"id\":1,\"ip\":\"149.154.175.53\"}," +
            "{\"id\":2,\"ip\":\"149.154.167.51\"}," +
            "{\"id\":3,\"ip\":\"149.154.175.100\"}," +
            "{\"id\":4,\"ip\":\"149.154.167.91\"}," +
            "{\"id\":5,\"ip\":\"91.108.56.130\"}]," +
            "\"direct_ws\":[" +
            "{\"dc\":2,\"ip\":\"149.154.167.220\",\"sni\":\"kws2.web.telegram.org\"}," +
            "{\"dc\":4,\"ip\":\"149.154.167.220\",\"sni\":\"kws4.web.telegram.org\"}]}," +
            "\"doh\":[\"https://1.1.1.1/dns-query\",\"https://8.8.8.8/resolve\"]," +
            "\"payload\":{\"version\":\"" + PayloadPin.Version + "\"," +
            "\"url\":\"" + PayloadPin.Url + "\"," +
            "\"sha256\":\"" + PayloadPin.Sha256 + "\"}}";

        internal static DataDocument Load()
        {
            string error;
            DataDocument document = DataDocumentParser.Parse(Json, out error);
            if (document == null)
            {
                throw new InvalidOperationException(
                    "Встроенный снимок данных повреждён: " + error);
            }
            return document;
        }
    }
}
