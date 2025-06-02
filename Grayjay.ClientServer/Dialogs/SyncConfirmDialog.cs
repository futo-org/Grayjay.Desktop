using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using System.Text.Json;
using static Grayjay.ClientServer.Controllers.StateUI;

namespace Grayjay.ClientServer.Dialogs
{
    public class SyncConfirmDialog : RemoteDialog
    {
        public string PublicKey { get; init; }
        private readonly Action<bool> _callback;

        public SyncConfirmDialog(string publicKey, Action<bool> callback): base("syncConfirm")
        {
            PublicKey = publicKey;
            _callback = callback;
        }

        [DialogMethod("cancel")]
        public void Dialog_Cancel(CustomDialog dialog, JsonElement parameter)
        {
            _callback(false);
            Close();
        }

        [DialogMethod("confirm")]
        public void Dialog_Confirm(CustomDialog dialog, JsonElement parameter)
        {
            _callback(true);
            Close();
        }
    }
}
