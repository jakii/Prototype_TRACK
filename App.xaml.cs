namespace TDMASApp
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        public static int CurrentUser { get; internal set; }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}