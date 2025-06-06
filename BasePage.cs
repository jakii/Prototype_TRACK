
public class BasePage : ContentPage
{
    public BasePage()
    {
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior
        {
            IsVisible = false,
            IsEnabled = false
        });
    }

    protected override bool OnBackButtonPressed()
    {
        return true; // Disables Android physical back
    }
}