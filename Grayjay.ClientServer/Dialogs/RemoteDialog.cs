using Grayjay.ClientServer.Controllers;
using System.Reflection;
using System.Text.Json;
using static Grayjay.ClientServer.Controllers.StateUI;

namespace Grayjay.ClientServer.Dialogs
{
    public class RemoteDialog
    {
        private string _dialogName;
        private CustomDialog _dialog;

        public string Status { get; set; }
        public bool IsOpen => _dialog != null;
        public event Action? OnClosed;

        public RemoteDialog(string name)
        {
            _dialogName = name;
        }

        public virtual async Task Show()
        {
            _dialog = await StateUI.DialogCustom(_dialogName, this,
                GetType().GetMethods()
                    .Where(x => x.GetCustomAttribute<DialogMethodAttribute>() != null)
                    .ToDictionary(
                        x => x.GetCustomAttribute<DialogMethodAttribute>().Name,
                        y => new Action<CustomDialog, JsonElement>((CustomDialog dialog, JsonElement obj) => y.Invoke(this, new object[] { dialog, obj }))));
        }

        public virtual void Update()
        {
            _dialog?.UpdateData(this);
        }
        public async Task CloseAsync()
        {
            var dlg = _dialog;
            _dialog = null;
            if (dlg != null)
            {
                OnClosed?.Invoke();
                try
                {
                    await dlg.Close().ConfigureAwait(false);
                }
                catch
                {
                    
                }
                dlg.Dispose();
            }
        }

        [DialogMethod("close")]
        public void Close(CustomDialog dialog, JsonElement parameter)
        {
            _ = CloseAsync();
        }

        public class DialogMethodAttribute: Attribute
        {
            public string Name { get; set; }

            public DialogMethodAttribute(string name)
            {
                Name = name;
            }
        }
    }
}
