namespace EasyMvvm.Core
{
    public interface IView
    {
        void Show();

        bool? ShowDialog();

        void OnSendMessageAsync(string message);

        void Close();
    }
}
