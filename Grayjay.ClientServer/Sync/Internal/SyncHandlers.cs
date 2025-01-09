using Grayjay.ClientServer.Serializers;
using Grayjay.Desktop.POC;
using System.Collections;
using System.Reflection;
using System.Text;
using static Grayjay.ClientServer.Sync.Internal.SyncSocketSession;

namespace Grayjay.ClientServer.Sync.Internal
{
    //While convenient AF, this shit is not gonna AOT compile. Worst case, keep this in Grayjay.Desktop
    public abstract class SyncHandlers
    {
        protected Dictionary<byte, Action<SyncSession, SyncSocketSession, byte, byte[]>> _handlers;
        protected Dictionary<byte, Func<SyncSession, SyncSocketSession, byte, byte[], Task>> _asyncHandlers;

        public SyncHandlers()
        {
            Type t = GetType();
            _handlers = t.GetMethods()
                .Where(x => x.GetCustomAttribute<SyncHandler>() != null)
                .Select(x => (x.GetCustomAttribute<SyncHandler>(), x))
                .DistinctBy(x => x.Item1!.SubOpcode)
                .ToDictionary(x => x.Item1!.SubOpcode, x => GetInvokeMethod(x.Item2));
            _asyncHandlers = t.GetMethods()
                .Where(x => x.GetCustomAttribute<SyncAsyncHandler>() != null)
                .Select(x => (x.GetCustomAttribute<SyncAsyncHandler>(), x))
                .DistinctBy(x => x.Item1!.SubOpcode)
                .ToDictionary(x => x.Item1!.SubOpcode, x => GetAsyncInvokeMethod(x.Item2));
        }

        //TODO: Support automatic responses on return value?
        private Action<SyncSession, SyncSocketSession, byte, byte[]> GetInvokeMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return new Action<SyncSession, SyncSocketSession, byte, byte[]>((ses, sock, opcode, data) =>
            {
                object[] paras = new object[parameters.Length];
                for(int i = 0; i < parameters.Length; i++)
                {
                    ParameterInfo param = parameters[i];
                    if (param.ParameterType == typeof(SyncSession))
                        paras[i] = ses;
                    else if (param.ParameterType == typeof(SyncSocketSession))
                        paras[i] = sock;
                    else if (param.ParameterType == typeof(byte[]))
                        paras[i] = data;
                    else if (param.ParameterType == typeof(string))
                        paras[i] = Encoding.UTF8.GetString(data);
                    else if(!param.ParameterType.IsPrimitive || param.ParameterType.IsArray || param.ParameterType.IsSubclassOf(typeof(ICollection)))
                        paras[i] = ConvertNativeObject(param.ParameterType, data);
                    else
                        throw new NotImplementedException($"Type {param.ParameterType.Name} not implemented for SyncHandler {method.Name}");
                }
                try
                {
                    method.Invoke(this, paras);
                }
                catch(Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Opcode {opcode} failed to execute due to: {ex.Message}\n{ex.StackTrace}");
                    Console.ResetColor();
                }
            });
        }

        private Func<SyncSession, SyncSocketSession, byte, byte[], Task> GetAsyncInvokeMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return new Func<SyncSession, SyncSocketSession, byte, byte[], Task>(async (ses, sock, opcode, data) =>
            {
                object[] paras = new object[parameters.Length];
                for(int i = 0; i < parameters.Length; i++)
                {
                    ParameterInfo param = parameters[i];
                    if (param.ParameterType == typeof(SyncSession))
                        paras[i] = ses;
                    else if (param.ParameterType == typeof(SyncSocketSession))
                        paras[i] = sock;
                    else if (param.ParameterType == typeof(byte[]))
                        paras[i] = data;
                    else if (param.ParameterType == typeof(string))
                        paras[i] = Encoding.UTF8.GetString(data);
                    else if(!param.ParameterType.IsPrimitive || param.ParameterType.IsArray || param.ParameterType.IsSubclassOf(typeof(ICollection)))
                        paras[i] = ConvertNativeObject(param.ParameterType, data);
                    else
                        throw new NotImplementedException($"Type {param.ParameterType.Name} not implemented for SyncHandler {method.Name}");
                }
                try
                {
                    var t = method.Invoke(this, paras);
                    if (t != null && t is Task task)
                        await task;
                }
                catch(Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Opcode {opcode} failed to execute due to: {ex.Message}\n{ex.StackTrace}");
                    Console.ResetColor();
                }
            });
        }
        //Default Json, can be overriden.
        protected virtual object ConvertNativeObject(Type targetType, byte[] data)
        {
            try
            {
                string json = Encoding.UTF8.GetString(data);
                return GJsonSerializer.AndroidCompatible.DeserializeObj(json, targetType);
            }
            catch(Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Sync conversion failed for type {targetType.Name}:\n" + ex.Message + "\n" + ex.StackTrace);
                Console.WriteLine($"DATA:\n" + Encoding.UTF8.GetString(data));
                Console.ResetColor();
                throw;
            }
        }

        public virtual void HandleAsync(SyncSession session, SyncSocketSession socket, byte opcode, byte subOpcode, byte[] data)
        {
            if (opcode != (byte)Opcode.DATA)
            {
                Logger.w<SyncHandlers>($"Only DATA opcodes allowed in SyncHandlers.Handle (opcode = {opcode}, subOpcode = {subOpcode})");
                return;
            }

            var handled = false;
            if (_handlers.TryGetValue(subOpcode, out var handler) && handler != null)
            {
                handler(session, socket, subOpcode, data);
                handled = true;
            }

            if (_asyncHandlers.TryGetValue(subOpcode, out var asyncHandler) && asyncHandler != null)
            {
                asyncHandler(session, socket, subOpcode, data);
                handled = true;
            }
            
            if (!handled)
                Logger.w<SyncHandlers>($"Unhandled opcode (opcode = {opcode}, subOpcode = {subOpcode})");
        }
    }

    public class SyncHandler : Attribute
    {
        public byte SubOpcode { get; set; }

        public SyncHandler(byte subOpcode)
        {
            SubOpcode = subOpcode;
        }
    }

    public class SyncAsyncHandler : Attribute
    {
        public byte SubOpcode { get; set; }

        public SyncAsyncHandler(byte subOpcode)
        {
            SubOpcode = subOpcode;
        }
    }
}
