using System.Linq;
using System.Threading.Tasks;
using Edelstein.Core;
using Edelstein.Core.Distributed.Peers.Info;
using Edelstein.Core.Gameplay.Constants;
using Edelstein.Database.Entities.Inventories;
using Edelstein.Database.Entities.Inventories.Items;
using Edelstein.Network.Packets;
using Edelstein.Service.Game.Conversations;
using Edelstein.Service.Game.Conversations.Speakers;
using Edelstein.Service.Game.Conversations.Speakers.Fields;
using Edelstein.Service.Game.Fields.Objects;
using Edelstein.Service.Game.Fields.Objects.Drops;

namespace Edelstein.Service.Game.Fields.User
{
    public partial class FieldUser
    {
        private async Task OnUserTransferFieldRequest(IPacket packet)
        {
            packet.Decode<byte>();
            packet.Decode<int>();

            var name = packet.Decode<string>();
            var portal = Field.GetPortal(name);

            await portal.Enter(this);
        }

        private async Task OnUserTransferChannelRequest(IPacket packet)
        {
            var channel = packet.Decode<byte>();

            try
            {
                var service = Socket.Service.Peers
                    .OfType<GameServiceInfo>()
                    .Where(g => g.WorldID == Socket.Service.Info.WorldID)
                    .OrderBy(g => g.ID)
                    .ToList()[channel];

                await Socket.TryMigrateTo(Socket.Account, Socket.Character, service);
            }
            catch
            {
                using (var p = new Packet(SendPacketOperations.TransferChannelReqIgnored))
                {
                    p.Encode<byte>(0x1);
                    await SendPacket(p);
                }
            }
        }

        private async Task OnUserMigrateToCashShopRequest(IPacket packet)
        {
            try
            {
                var service = Socket.Service.Peers
                    .OfType<ShopServiceInfo>()
                    .Where(g => g.Worlds.Contains(Socket.Service.Info.WorldID))
                    .OrderBy(g => g.ID)
                    .First();

                // TODO: multi selection
                await Socket.TryMigrateTo(Socket.Account, Socket.Character, service);
            }
            catch
            {
                using (var p = new Packet(SendPacketOperations.TransferChannelReqIgnored))
                {
                    p.Encode<byte>(0x2);
                    await SendPacket(p);
                }
            }
        }

        private async Task OnUserMove(IPacket packet)
        {
            packet.Decode<long>();
            packet.Decode<byte>();
            packet.Decode<long>();
            packet.Decode<int>();
            packet.Decode<int>();
            packet.Decode<int>();

            var path = Move(packet);

            using (var p = new Packet(SendPacketOperations.UserMove))
            {
                p.Encode<int>(ID);
                path.Encode(p);
                await Field.BroadcastPacket(this, p);
            }
        }

        private async Task OnUserChat(IPacket packet)
        {
            packet.Decode<int>();

            var message = packet.Decode<string>();
            var onlyBalloon = packet.Decode<bool>();

            using (var p = new Packet(SendPacketOperations.UserChat))
            {
                p.Encode<int>(ID);
                p.Encode<bool>(false);
                p.Encode<string>(message);
                p.Encode<bool>(onlyBalloon);
                await Field.BroadcastPacket(p);
            }
        }

        private async Task OnUserEmotion(IPacket packet)
        {
            var emotion = packet.Decode<int>();
            var duration = packet.Decode<int>();
            var byItemOption = packet.Decode<bool>();

            // TODO: item option checks

            using (var p = new Packet(SendPacketOperations.UserEmotion))
            {
                p.Encode<int>(ID);
                p.Encode<int>(emotion);
                p.Encode<int>(duration);
                p.Encode<bool>(byItemOption);
                await Field.BroadcastPacket(this, p);
            }
        }

        private async Task OnUserSelectNPC(IPacket packet)
        {
            var npc = Field.GetObject<FieldNPC>(packet.Decode<int>());

            if (npc == null) return;

            var template = npc.Template;
            var script = template.Scripts.FirstOrDefault()?.Script;

            if (script == null) return;

            var context = new ConversationContext(Socket);
            var conversation = await Service.ConversationManager.Build(
                script,
                context,
                new FieldNPCSpeaker(context, npc),
                new FieldUserSpeaker(context, this)
            );

            await Converse(conversation);
        }

        private async Task OnUserScriptMessageAnswer(IPacket packet)
        {
            if (ConversationContext == null) return;

            var type = (ConversationMessageType) packet.Decode<byte>();

            if (type != ConversationContext.LastRequestType) return;
            if (type == ConversationMessageType.AskQuiz ||
                type == ConversationMessageType.AskSpeedQuiz)
            {
                await ConversationContext.Respond(packet.Decode<string>());
                return;
            }

            var answer = packet.Decode<byte>();

            if (
                type != ConversationMessageType.Say &&
                type != ConversationMessageType.AskYesNo &&
                type != ConversationMessageType.AskAccept &&
                answer == byte.MinValue ||
                (type == ConversationMessageType.Say ||
                 type == ConversationMessageType.AskYesNo ||
                 type == ConversationMessageType.AskAccept) && answer == byte.MaxValue
            )
            {
                ConversationContext.TokenSource.Cancel();
                return;
            }

            switch (type)
            {
                case ConversationMessageType.AskText:
                case ConversationMessageType.AskBoxText:
                    await ConversationContext.Respond(packet.Decode<string>());
                    break;
                case ConversationMessageType.AskNumber:
                case ConversationMessageType.AskMenu:
                case ConversationMessageType.AskSlideMenu:
                    await ConversationContext.Respond(packet.Decode<int>());
                    break;
                case ConversationMessageType.AskAvatar:
                case ConversationMessageType.AskMemberShopAvatar:
                    await ConversationContext.Respond(packet.Decode<byte>());
                    break;
                default:
                    await ConversationContext.Respond(answer);
                    break;
            }
        }

        private async Task OnUserChangeSlotPositionRequest(IPacket packet)
        {
            packet.Decode<int>();
            var type = (ItemInventoryType) packet.Decode<byte>();
            var from = packet.Decode<short>();
            var to = packet.Decode<short>();
            var number = packet.Decode<short>();

            if (to == 0)
            {
                await ModifyInventory(i =>
                {
                    var item = Character.Inventories[type].Items[from];

                    if (!ItemConstants.IsTreatSingly(item.TemplateID))
                    {
                        if (!(item is ItemSlotBundle bundle)) return;
                        if (bundle.Number < number) return;

                        item = i[type].Take(from, number);
                    }
                    else i[type].Remove(from);

                    var drop = new ItemFieldDrop(item) {Position = Position};
                    Field.Enter(drop, () => drop.GetEnterFieldPacket(0x1, this));
                }, true);
                return;
            }

            // TODO: equippable checks
            await ModifyInventory(i => i[type].Move(from, to), true);
        }

        private Task OnDropPickUpRequest(IPacket packet)
        {
            packet.Decode<byte>();
            packet.Decode<int>();
            packet.Decode<short>();
            packet.Decode<short>();
            var objectID = packet.Decode<int>();
            packet.Decode<int>();
            var drop = Field.GetObject<AbstractFieldDrop>(objectID);

            return drop?.PickUp(this);
        }

        private async Task OnUserPortalScriptRequest(IPacket packet)
        {
            packet.Decode<byte>();

            var name = packet.Decode<string>();
            var portal = Field.Template.Portals.Values.FirstOrDefault(p => p.Name.Equals(name));

            if (portal == null) return;
            if (string.IsNullOrEmpty(portal.Script)) return;

            var context = new ConversationContext(Socket);
            var conversation = await Socket.Service.ConversationManager.Build(
                portal.Script,
                context,
                new FieldSpeaker(context, Field),
                new FieldUserSpeaker(context, this)
            );

            await Converse(conversation);
        }
    }
}