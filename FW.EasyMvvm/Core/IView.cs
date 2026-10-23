namespace FW.EasyMvvm.Core
{
    public interface IView
    {
        void Show();

        bool? ShowDialog();

        void OnSendMessage(string message);

        void Close();
    }
}
