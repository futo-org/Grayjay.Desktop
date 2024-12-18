using Grayjay.ClientServer.States;
using System.Text.Json;
using static Grayjay.ClientServer.Controllers.StateUI;

namespace Grayjay.ClientServer.Dialogs
{
    public class SyncConfirmDialog : RemoteDialog
    {
        public string PublicKey { get; init; }

        public SyncConfirmDialog(string publicKey): base("syncConfirm")
        {
            PublicKey = publicKey;
        }

        [DialogMethod("cancel")]
        public void Dialog_Cancel(CustomDialog dialog, JsonElement parameter)
        {
            StateSync.Instance.GetSession(PublicKey)?.Unauthorize();
            Close();
        }

        [DialogMethod("confirm")]
        public void Dialog_Confirm(CustomDialog dialog, JsonElement parameter)
        {
            StateSync.Instance.GetSession(PublicKey)?.Authorize();
            Close();
        }
    }
}
