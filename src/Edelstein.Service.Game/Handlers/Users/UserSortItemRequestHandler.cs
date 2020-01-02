using System.Linq;
using System.Threading.Tasks;
using Edelstein.Core.Utils;
using Edelstein.Core.Utils.Packets;
using Edelstein.Entities.Inventories;
using Edelstein.Network.Packets;
using Edelstein.Service.Game.Fields.Objects.User;

namespace Edelstein.Service.Game.Handlers.Users
{
    public class UserSortItemRequestHandler : AbstractFieldUserHandler
    {
        protected override async Task Handle(
            FieldUser user,
            RecvPacketOperations operation,
            IPacket packet
        )
        {
            packet.Decode<int>();

            var inventoryType = (ItemInventoryType) packet.Decode<byte>();
            var inventoryCopy = user.Character.Inventories[inventoryType].Items
                .Where(kv => kv.Key > 0)
                .OrderBy(kv => kv.Value.TemplateID)
                .ToList();
            short position = 1;

            await user.ModifyInventory(i =>
            {
                inventoryCopy.ForEach(kv => i.Remove(kv.Value));
                inventoryCopy.ForEach(kv => i.Set(position++, kv.Value));
            }, true);

            using var p = new Packet(SendPacketOperations.SortItemResult);
            p.Encode<bool>(false);
            p.Encode<byte>((byte) inventoryType);
            await user.SendPacket(p);
        }
    }
}