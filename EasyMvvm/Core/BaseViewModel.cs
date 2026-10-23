
namespace EasyMvvm.Core
{
    public abstract class BaseViewModel : BaseEntity
    {
        public BaseModel Model { get; set; }
        public IView View { get; set; }

        public void SetModel(BaseModel model, IView view)
        {
            View = view;
            Model = model;
            Model.ViewModel = this;
            SendCommand += model.SendCommand;
            InitializeViewModel();
        }

        /// <summary>
        /// Если что-то дополнительно надо инициализировать,
        /// то делать это здесь.
        /// </summary>
        public virtual void InitializeViewModel() { }

        #region Commands

        public delegate void CommandHandler(object parameters);
        public event CommandHandler SendCommand;

        public RelayCommand Cmd => new(obj => SendCommand(obj));

        #endregion
    }
}
