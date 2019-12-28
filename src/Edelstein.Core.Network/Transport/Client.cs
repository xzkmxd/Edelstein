using System.Net;
using System.Threading.Tasks;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Edelstein.Network.Codecs;
using Edelstein.Network.Logging;

namespace Edelstein.Network.Transport
{
    public class Client
    {
        private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

        public ISocketAdapterFactory AdapterFactory { get; }
        public short Version { get; }
        public string Patch { get; }
        public byte Locale { get; }

        private IChannel? Channel { get; set; }
        private IEventLoopGroup? WorkerGroup { get; set; }
        
        public ISocket? Socket { get; protected internal set; }

        public Client(ISocketAdapterFactory adapterFactory, short version, string patch, byte locale)
        {
            AdapterFactory = adapterFactory;
            Version = version;
            Patch = patch;
            Locale = locale;
        }

        public async Task Start(string host, int port)
        {
            WorkerGroup = new MultithreadEventLoopGroup();
            Channel = await new Bootstrap()
                .Group(WorkerGroup)
                .Channel<TcpSocketChannel>()
                .Option(ChannelOption.TcpNodelay, true)
                .Handler(new ActionChannelInitializer<IChannel>(ch =>
                {
                    ch.Pipeline.AddLast(
                        new PacketDecoder(Version),
                        new ClientAdapter(this),
                        new PacketEncoder(Version)
                    );
                }))
                .ConnectAsync(IPAddress.Parse(host), port);

            Logger.Info($"Established connection to {host}:{port}");
        }

        public async Task Stop()
        {
            await Channel.CloseAsync();
            await WorkerGroup.ShutdownGracefullyAsync();
        }
    }
}