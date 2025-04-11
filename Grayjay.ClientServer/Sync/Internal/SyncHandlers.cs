using Grayjay.ClientServer.Casting;
using Grayjay.ClientServer.Serializers;
using SyncClient;
using System.Collections;
using System.Reflection;
using System.Text;

using Logger = Grayjay.Desktop.POC.Logger;
using Opcode = SyncShared.Opcode;

namespace Grayjay.ClientServer.Sync.Internal
{
    //While convenient AF, this shit is not gonna AOT compile. Worst case, keep this in Grayjay.Desktop
    public abstract class SyncHandlers
    {
        protected Dictionary<byte, Action<SyncSession, byte, ReadOnlySpan<byte>>> _handlers;
        protected Dictionary<byte, Func<SyncSession, byte, byte[], Task>> _asyncHandlers;

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
        private Action<SyncSession, byte, ReadOnlySpan<byte>> GetInvokeMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return new Action<SyncSession, byte, ReadOnlySpan<byte>>((ses, opcode, data) =>
            {
                object[] paras = new object[parameters.Length];
                for(int i = 0; i < parameters.Length; i++)
                {
                    ParameterInfo param = parameters[i];
                    if (param.ParameterType == typeof(SyncSession))
                        paras[i] = ses;
                    else if (param.ParameterType == typeof(byte[]))
                        paras[i] = data.ToArray();
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
                    Logger.Error<SyncHandlers>($"Opcode {opcode} failed to execute", ex);
                }
            });
        }

        private Func<SyncSession, byte, byte[], Task> GetAsyncInvokeMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return new Func<SyncSession, byte, byte[], Task>(async (ses, opcode, data) =>
            {
                object[] paras = new object[parameters.Length];
                for(int i = 0; i < parameters.Length; i++)
                {
                    ParameterInfo param = parameters[i];
                    if (param.ParameterType == typeof(SyncSession))
                        paras[i] = ses;
                    else if (param.ParameterType == typeof(ReadOnlySpan<byte>))
                        paras[i] = data.ToArray();
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
                    Logger.Error<SyncHandlers>($"Opcode {opcode} failed", ex);
                }
            });
        }
        //Default Json, can be overriden.
        protected virtual object ConvertNativeObject(Type targetType, ReadOnlySpan<byte> data)
        {
            try
            {
                string json = Encoding.UTF8.GetString(data);
                return GJsonSerializer.AndroidCompatible.DeserializeObj(json, targetType);
            }
            catch(Exception ex)
            {
                Logger.Error<SyncHandlers>($"Sync conversion failed for type {targetType.Name}\nDATA:\n" + Encoding.UTF8.GetString(data), ex);
                throw;
            }
        }

        public virtual void Handle(SyncSession session, Opcode opcode, byte subOpcode, ReadOnlySpan<byte> data)
        {
            if (opcode != Opcode.DATA)
            {
                Logger.w<SyncHandlers>($"Only DATA opcodes allowed in SyncHandlers.Handle (opcode = {opcode}, subOpcode = {subOpcode})");
                return;
            }

            var handled = false;
            if (_handlers.TryGetValue(subOpcode, out var handler) && handler != null)
            {
                handler(session, subOpcode, data);
                handled = true;
            }

            if (_asyncHandlers.TryGetValue(subOpcode, out var asyncHandler) && asyncHandler != null)
            {
                var d = data.ToArray();
                _ = Task.Run(async () =>
                {
                    await asyncHandler(session, subOpcode, d);
                });
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
