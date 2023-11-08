using Craftimizer.Plugin;
using Craftimizer.Plugin.Utils;
using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;

namespace Craftimizer.Utils;

public static class FoodStatus
{
    private static readonly ReadOnlyDictionary<uint, uint> ItemFoodToItemLUT;
    private static readonly ReadOnlyDictionary<uint, Food> FoodItems;
    private static readonly ReadOnlyDictionary<uint, Food> MedicineItems;
    private static readonly ImmutableArray<uint> FoodOrder;
    private static readonly ImmutableArray<uint> MedicineOrder;

    public readonly record struct FoodStat(bool IsRelative, int Value, int Max, int ValueHQ, int MaxHQ);
    public readonly record struct Food(Item Item, FoodStat? Craftsmanship, FoodStat? Control, FoodStat? CP);

    static FoodStatus()
    {
        var lut = new Dictionary<uint, uint>();
        foreach (var item in LuminaSheets.ItemSheet)
        {
            var isFood = item.ItemUICategory.Row == 46;
            var isMedicine = item.ItemUICategory.Row == 44;
            if (!isFood && !isMedicine)
                continue;

            if (item.ItemAction.Value == null)
                continue;

            if (!(item.ItemAction.Value.Type is 844 or 845 or 846))
                continue;

            var itemFood = LuminaSheets.ItemFoodSheet.GetRow(item.ItemAction.Value.Data[1]);
            if (itemFood == null)
                continue;

            lut.TryAdd(itemFood.RowId, item.RowId);
        }
        ItemFoodToItemLUT = lut.AsReadOnly();

        var foods = new Dictionary<uint, Food>();
        var medicines = new Dictionary<uint, Food>();
        foreach (var item in LuminaSheets.ItemSheet)
        {
            var isFood = item.ItemUICategory.Row == 46;
            var isMedicine = item.ItemUICategory.Row == 44;
            if (!isFood && !isMedicine)
                continue;

            if (item.ItemAction.Value == null)
                continue;

            if (!(item.ItemAction.Value.Type is 844 or 845 or 846))
                continue;

            var itemFood = LuminaSheets.ItemFoodSheet.GetRow(item.ItemAction.Value.Data[1]);
            if (itemFood == null)
                continue;

            FoodStat? craftsmanship = null, control = null, cp = null;
            foreach (var stat in itemFood.UnkData1)
            {
                if (stat.BaseParam == 0)
                    continue;
                var foodStat = new FoodStat(stat.IsRelative, stat.Value, stat.Max, stat.ValueHQ, stat.MaxHQ);
                switch (stat.BaseParam)
                {
                    case Gearsets.ParamCraftsmanship: craftsmanship = foodStat; break;
                    case Gearsets.ParamControl: control = foodStat; break;
                    case Gearsets.ParamCP: cp = foodStat; break;
                    default: continue;
                }
            }

            if (craftsmanship != null || control != null || cp != null)
            {
                var food = new Food(item, craftsmanship, control, cp);
                if (isFood)
                    foods.Add(item.RowId, food);
                if (isMedicine)
                    medicines.Add(item.RowId, food);
            }
        }
        
        FoodItems = foods.AsReadOnly();
        MedicineItems = medicines.AsReadOnly();

        FoodOrder = FoodItems.OrderByDescending(a => a.Value.Item.LevelItem.Row).Select(a => a.Key).ToImmutableArray();
        MedicineOrder = MedicineItems.OrderByDescending(a => a.Value.Item.LevelItem.Row).Select(a => a.Key).ToImmutableArray();
    }

    public static void Initialize() { }

    public static IEnumerable<Food> OrderedFoods => FoodOrder.Select(id => FoodItems[id]);
    public static IEnumerable<Food> OrderedMedicines => MedicineOrder.Select(id => MedicineItems[id]);

    public static (uint ItemId, bool IsHQ)? ResolveFoodParam(ushort param)
    {
        var isHq = param > 10000;
        param -= 10000;

        if (!ItemFoodToItemLUT.TryGetValue(param, out var itemId))
            return null;

        return (itemId, isHq);
    }

    public static Food? TryGetFood(uint itemId)
    {
        if (FoodItems.TryGetValue(itemId, out var food))
            return food;
        if (MedicineItems.TryGetValue(itemId, out food))
            return food;
        return null;
    }
}
