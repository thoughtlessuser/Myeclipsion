using Content.Shared._Crescent.DegradeableArmor;
using Content.Shared.Inventory;
using Content.Shared.Rejuvenate;

namespace Content.Server.Administration.Systems;

public sealed class RejuvenateSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public void PerformRejuvenate(EntityUid target)
    {
        RaiseLocalEvent(target, new RejuvenateEvent());

        // Repair all worn degradeable armor
        var slots = _inventory.GetSlotEnumerator(target);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } entity && TryComp<DegradeableArmorComponent>(entity, out var armor))
            {
                armor.armorHealth = armor.armorMaxHealth;
                Dirty(entity, armor);
            }
        }
    }
}
