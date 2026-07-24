namespace HaberOtomasyon
{
    /// <summary>
    /// Tüm ayarlar burada toplanıyor. Bir şeyi değiştirmen gerektiğinde
    /// her seferinde SADECE bu dosyaya bakman yeterli.
    /// </summary>
    public static class Config
    {
        // --- Vast.ai erişim bilgileri ---
        public const string VastApiKey = "ea2a38cb00d588686558dea3d2503bae5171f571b64e3c985c091d8350d5fb7e";
        public const string VastInstanceId = "45321248";
        public const int ComfyUiInternalPort = 8188;
        public const int OllamaInternalPort = 8288; // Vast.ai Portal / Caddy Ollama portu

        // Terminalde "echo $OPEN_BUTTON_TOKEN" ile alınır. Instance her yeniden oluşturulduğunda
        // (silinip yeni kiralandığında) DEĞİŞİR - stop/start'ta aynı kalıp kalmadığını takip et.
        public const string OpenButtonToken = "054789dae36a0e6303cf65e1c874b3d6fd40f7c7f4d07346546aa11df477d86b";

        // --- Ollama (yerel LLM) ayarları ---
        public const string OllamaBaseUrl = "http://127.0.0.1:8288";
        public const string OllamaModelName = "llama3.1:8b";

        // --- Workflow JSON dosya yolları ---
        public const string TtsWorkflowPath = "tts_workflow.json";
        public const string LipsyncWorkflowPath = "lipsync_workflow.json";

        // --- TTS (Chatterbox Multilingual) node ID'leri ---
        public const string TtsNodeIdAudioPrompt = "72";
        public const string TtsNodeIdMultilingual = "73";
        public const string TtsNodeIdPreviewAudio = "74";

        // --- Lipsync (FLOAT) node ID'leri ---
        public const string LipsyncNodeIdImage = "3";
        public const string LipsyncNodeIdAudio = "4";
        public const string LipsyncNodeIdVideoCombine = "6";

        // --- RSS kaynağı ---
        public const string NewsRssUrl = "https://feeds.bbci.co.uk/turkce/rss.xml";

        // --- Sabit dosya yolları ---
        public const string ReferenceVoicePath = "/home/onur/Desktop/myLora/deneme/ses.ogg";
        public const string ReferenceImagePath = "/home/onur/Desktop/myLora/deneme/foto.jpeg";
        public const string NewsTextFilePath = "/home/onur/Desktop/myLora/deneme/haber_metni.txt";
        public const string GeneratedAudioOutputPath = "/home/onur/Desktop/myLora/deneme/output/generated_voice.flac";
        public const string FinalVideoOutputPath = "/home/onur/Desktop/myLora/deneme/output/generated_lipsync.mp4";

        // --- Timeout ayarları (saniye) ---
        public const int InstanceBootTimeoutSeconds = 300;
        public const int ComfyUiReadyTimeoutSeconds = 480;
        public const int JobTimeoutSeconds = 1800;
    }
}
