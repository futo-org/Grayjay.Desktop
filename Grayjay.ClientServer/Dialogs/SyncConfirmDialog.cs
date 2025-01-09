using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
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
        public async void Dialog_Cancel(CustomDialog dialog, JsonElement parameter)
        {
            var session = StateSync.Instance.GetSession(PublicKey);

            try
            {
                if (session != null)
                    await session.UnauthorizeAsync();
            }
            catch (Exception e)
            {
                Logger.i<SyncConfirmDialog>("Failed to send Unauthorize", e);
            }
            
            Close();
        }

        [DialogMethod("confirm")]
        public async void Dialog_Confirm(CustomDialog dialog, JsonElement parameter)
        {
            var session = StateSync.Instance.GetSession(PublicKey);

            try
            {
                if (session != null)
                    await session.AuthorizeAsync();
            }
            catch (Exception e)
            {
                Logger.i<SyncConfirmDialog>("Failed to send Authorize", e);
            }

            Close();
        }
    }
}
