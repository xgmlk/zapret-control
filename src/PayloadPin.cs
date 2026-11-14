namespace ZapretControl
{
    // Pinned first-install payload. A clean machine gets these exact bytes and
    // verifies SHA256 before extraction. Later Flowseal updates come from
    // release.json with their own SHA256.
    public static class PayloadPin
    {
        public const string Version = "1.10.4";
        public const string Url =
            "https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.4/zapret-discord-youtube-1.10.4.zip";
        public const string Sha256 =
            "291AF673BD0238506F9548E0E94CA0ABF244000F4C9A06960544FB01C5F97B34";
        public const long Size = 1518649;
    }
}
