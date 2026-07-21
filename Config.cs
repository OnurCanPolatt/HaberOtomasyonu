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

        // Terminalde "echo $OPEN_BUTTON_TOKEN" ile alınır. Instance her yeniden oluşturulduğunda
        // (silinip yeni kiralandığında) DEĞİŞİR - stop/start'ta aynı kalıp kalmadığını takip et.
        public const string OpenButtonToken = "054789dae36a0e6303cf65e1c874b3d6fd40f7c7f4d07346546aa11df477d86b";

        // --- Workflow JSON dosya yolları (proje klasöründe, workflows/ altında) ---
        public const string TtsWorkflowPath = "tts_workflow.json";
        public const string LipsyncWorkflowPath = "lipsync_workflow.json";

        // --- TTS (Chatterbox Multilingual) node ID'leri ---
        // Chatterbox(1).json dosyandaki node 25 = FL_ChatterboxMultilingualTTS (Türkçe destekli)
        public const string TtsNodeIdAudioPrompt = "72";   // LoadAudio (referans ses)
        public const string TtsNodeIdMultilingual = "73";  // FL_ChatterboxMultilingualTTS
        public const string TtsNodeIdPreviewAudio = "74";  // PreviewAudio (çıktının bağlı olduğu node)

        // --- Lipsync (FLOAT) node ID'leri ---
        public const string LipsyncNodeIdImage = "3";
        public const string LipsyncNodeIdAudio = "4";
        public const string LipsyncNodeIdVideoCombine = "6";

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
