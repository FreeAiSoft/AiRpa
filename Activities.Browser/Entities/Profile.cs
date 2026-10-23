using System.Collections.ObjectModel;

namespace Activities.Browser.Entities
{
    #region Enums

    public enum ProfileState { Closed, Run, Error, CookiesExport, CookiesImport }

    public enum WebRTCMode { ReplacementIP, DisableWebRTC, Normal }

    public enum CanvasMode { EditCanvas, DisableWebCanvas, Normal }

    public enum WebGLMode { AddNoise, DisableWebGL, Normal }

    public enum AudiocontextMode { AddNoise, Normal }

    public enum TZMode { Normal, OverrideHand }

    public enum LocaleMode { Normal, Override }

    public enum FontsMode { Custom, Normal }

    public enum GeoMode { OverrideAuto, Normal }

    public enum AcceptLanguageMode { Normal, Override }

    #endregion Enums

    public class Profile
    {

        #region Fields
        private ObservableCollection<string> acceptLanguages = new ObservableCollection<string>();

        #endregion Fields

        #region Properties

        public string Title { get; set; }

        public string Description { get; set; }

        public string ProfilePath { get; set; }

        public string UserAgent { get; set; }

        public string HashString { get; set; }

        public string AESKey { get; set; }

        public byte[] AESKeyInBytes { get; set; }

        public Proxy Proxy { get; set; }

        public string AvatarPath { get; set; }

        public WebRTCMode WebRTCMode { get; set; }

        public CanvasMode CanvasMode { get; set; }

        public WebGLMode WebGLMode { get; set; }

        public AudiocontextMode AudiocontextMode { get; set; }

        public TZMode TzMode { get; set; }

        public TZ Tz { get; set; }

        public int TzSelectedIndex { get; set; }

        public string Locale { get; set; }

        public LocaleMode LocaleMode { get; set; }

        public string ScriptPath { get; set; }

        public AcceptLanguageMode AcceptLanguageMode { get; set; }

        public ObservableCollection<string> AcceptLanguages
        {
            get
            {
                if (acceptLanguages == null)
                {
                    acceptLanguages = new ObservableCollection<string>();
                }
                return acceptLanguages;
            }

            set => acceptLanguages = value;
        }

        #endregion Properties

        #region Methods

        public override string ToString()
        {
            return $"{Title} {Description} " + (Proxy == null ? "" : Proxy.ToString());
        }

        #endregion Methods
    }
}
