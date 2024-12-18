using Grayjay.ClientServer.Models;
using Grayjay.Engine.Exceptions;

namespace Grayjay.ClientServer.Exceptions
{
    public class DialogException: Exception
    {
        public ExceptionModel Model { get; set; }

        public DialogException(ExceptionModel model, Exception ex): base(ex.Message, ex)
        {
            if (model.TypeName == null && ex != null)
                model.TypeName = ex.GetType().Name;
            Model = model;
        }
        public DialogException(ExceptionModel model) : base(model.Message ?? model.Title)
        {
            Model = model;
        }

        public static DialogException FromException(string title, Exception ex)
        {
            if (ex is ScriptException)
                return new DialogException(ExceptionModel.FromException(ex), ex);
            string code = null;
            if (ex is ScriptException)
                code = ex.Source;
            return new DialogException(new ExceptionModel()
            {
                Type = nameof(DialogException),
                Title = title,
                Message = ex.Message,
                Code = code,
                Stacktrace = ex.StackTrace
            }, ex);
        }
    }
}
