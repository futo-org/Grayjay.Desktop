using Noise;
using SyncClient;
using SyncShared;
using System.Diagnostics;
using System.IO.Pipes;

namespace Grayjay.Desktop.Tests
{
    [TestClass]
    public class SyncTests
    {
        private static byte[] GenerateRandomByteArray(int length)
        {
            Random random = new Random();
            byte[] byteArray = new byte[length];
            random.NextBytes(byteArray);
            return byteArray;
        }

        private class Authorized : IAuthorizable
        {
            public bool IsAuthorized => true;
        }

        [TestMethod]
        public async Task TestSyncSessionHandshakeAndCommunication()
        {
            var initiatorPipeOut = new AnonymousPipeServerStream(PipeDirection.Out);
            var responderPipeIn = new AnonymousPipeClientStream(PipeDirection.In, initiatorPipeOut.ClientSafePipeHandle);
            
            var responderPipeOut = new AnonymousPipeServerStream(PipeDirection.Out);
            var initiatorPipeIn = new AnonymousPipeClientStream(PipeDirection.In, responderPipeOut.ClientSafePipeHandle);

            var initiatorOutput = initiatorPipeOut;
            var initiatorInput = initiatorPipeIn;
            var responderOutput = responderPipeOut;
            var responderInput = responderPipeIn;

            // Events to track when handshake and communication are complete
            var handshakeInitiatorCompleted = new ManualResetEventSlim(false);
            var handshakeResponderCompleted = new ManualResetEventSlim(false);

            // Generate key pairs for the initiator and responder
            var initiatorKeyPair = KeyPair.Generate();
            var responderKeyPair = KeyPair.Generate();

            var randomBytesExactlyOnePacket = GenerateRandomByteArray(SyncSocketSession.MAXIMUM_PACKET_SIZE - SyncSocketSession.HEADER_SIZE);
            var randomBytes = GenerateRandomByteArray(2 * (SyncSocketSession.MAXIMUM_PACKET_SIZE - SyncSocketSession.HEADER_SIZE));
            var randomBytesBig = GenerateRandomByteArray(SyncStream.MAXIMUM_SIZE);

            // Create and start the initiator session
            var initiatorSession = new SyncSocketSession("", initiatorKeyPair,
                initiatorInput, initiatorOutput,
                onClose: (session) => Console.WriteLine("Initiator session closed"),
                onNewChannel: (session, channel) => { },
                onHandshakeComplete: (session) =>
                {
                    Console.WriteLine("Initiator handshake complete");
                    handshakeInitiatorCompleted.Set();  // Handshake complete for initiator
                },
                onData: (session, opcode, subOpcode, data) => 
                {
                    Console.WriteLine($"Initiator received: Opcode {opcode}, Subopcode {subOpcode}, Data.Length: {data.Length}");
                    if (data.Length == randomBytesExactlyOnePacket.Length)
                    {
                        Assert.IsTrue(randomBytesExactlyOnePacket.AsSpan().SequenceEqual(data));
                        Console.WriteLine("randomBytesExactlyOnePacket valid");
                    }
                    else if (data.Length == randomBytes.Length)
                    {
                        Assert.IsTrue(randomBytes.AsSpan().SequenceEqual(data));
                        Console.WriteLine("randomBytes valid");
                    }
                    else if (data.Length == randomBytesBig.Length)
                    {
                        Assert.IsTrue(randomBytesBig.AsSpan().SequenceEqual(data));
                        Console.WriteLine("randomBytesBig valid");
                    }
                });

            // Create and start the responder session
            var responderSession = new SyncSocketSession("", responderKeyPair,
                responderInput, responderOutput,
                onClose: (session) => Console.WriteLine("Responder session closed"),
                onNewChannel: (session, channel) => { },
                onHandshakeComplete: (session) =>
                {
                    Console.WriteLine("Responder handshake complete");
                    handshakeResponderCompleted.Set();  // Handshake complete for responder
                },
                onData: (session, opcode, subOpcode, data) => 
                {
                    Console.WriteLine($"Responder received: Opcode {opcode}, Subopcode {subOpcode}, Data.Length: {data.Length}");
                    if (data.Length == randomBytesExactlyOnePacket.Length)
                    {
                        Assert.IsTrue(randomBytesExactlyOnePacket.AsSpan().SequenceEqual(data));
                        Console.WriteLine("randomBytesExactlyOnePacket valid");
                    }
                    else if (data.Length == randomBytes.Length)
                    {
                        Assert.IsTrue(randomBytes.AsSpan().SequenceEqual(data));
                        Console.WriteLine("randomBytes valid");
                    }
                    else if (data.Length == randomBytesBig.Length)
                    {
                        Assert.IsTrue(randomBytes.AsSpan().SequenceEqual(data));
                        Console.WriteLine("randomBytesBig valid");
                    }
                });

            // Start the sessions in parallel using tasks
            await initiatorSession.StartAsInitiatorAsync(responderSession.LocalPublicKey);
            await responderSession.StartAsResponderAsync();

            WaitHandle.WaitAll([handshakeInitiatorCompleted.WaitHandle, handshakeResponderCompleted.WaitHandle], 10000);

            initiatorSession.Authorizable = new Authorized();
            responderSession.Authorizable = new Authorized();

            // Simulate initiator sending a PING and responder replying with PONG
            await initiatorSession.SendAsync((byte)Opcode.PING);
            await responderSession.SendAsync((byte)Opcode.DATA, 0, randomBytesExactlyOnePacket);

            await initiatorSession.SendAsync((byte)Opcode.DATA, 1, randomBytes);

            var sw = Stopwatch.StartNew();
            await responderSession.SendAsync((byte)Opcode.DATA, 0, randomBytesBig);
            Console.WriteLine($"Sent 10MB in {sw.ElapsedMilliseconds}ms");

            // Wait for a brief period to simulate some delay and allow communication
            Thread.Sleep(1000);
        }
    }
}