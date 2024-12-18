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
        public void Close()
        {
            _dialog?.Close();
            _dialog = null;
        }


        [DialogMethod("close")]
        public void Close(CustomDialog dialog, JsonElement parameter)
        {
            Close();
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
