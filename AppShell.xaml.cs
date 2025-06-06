namespace TDMASApp
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
        }
        private async void OnLogoutClicked(object sender, System.EventArgs e)
        {
            //Confirm logout
            bool answer = await DisplayAlert("Logout", "Are you sure you want to logout?", "Yes", "No");

            if (answer)
            {
                Shell.Current.FlyoutBehavior = FlyoutBehavior.Disabled;

                await Shell.Current.GoToAsync("//MainPage");

                Shell.Current.Navigation.RemovePage(this);
            }
        }


    }
}
