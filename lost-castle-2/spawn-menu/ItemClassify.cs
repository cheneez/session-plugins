using System.Text.RegularExpressions;

namespace LC2SpawnMenu;

internal static class ItemClassify
{
    static readonly Regex ManualTier = new(@"^([A-Za-z]+)(\d+)-", RegexOptions.Compiled);

    static readonly string[] Qualities =
    {
        "Original", "Normal", "Uncommon", "Rare", "Epic", "Legendary"
    };

    public static void Apply(CatalogItem item)
    {
        var id = item.id ?? "";
        var cat = item.category ?? "";
        var manual = item.manual ?? "";

        if (cat.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
            item.family = "Weapon";
        else if (cat.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0)
            item.family = "Armor";
        else if (cat.IndexOf("Treasure", StringComparison.OrdinalIgnoreCase) >= 0
                 || cat.IndexOf("Passive", StringComparison.OrdinalIgnoreCase) >= 0)
            item.family = "Treasure";
        else
            item.family = "Other";

        item.type = item.family switch
        {
            "Weapon" => WeaponType(id),
            "Armor" => ArmorType(id),
            "Treasure" => TreasureType(id, manual),
            _ => string.IsNullOrEmpty(cat) ? "Other" : cat
        };

        ParseRank(manual, out var rank, out var quality);
        item.rank = rank;
        item.quality = quality;
    }

    static string WeaponType(string id)
    {
        if (id.StartsWith("THW", StringComparison.OrdinalIgnoreCase)) return "Two-Hand";
        if (id.StartsWith("SHW", StringComparison.OrdinalIgnoreCase)) return "One-Hand";
        if (id.StartsWith("Staff", StringComparison.OrdinalIgnoreCase)) return "Staff";
        if (id.StartsWith("Gun", StringComparison.OrdinalIgnoreCase) || id.StartsWith("MS", StringComparison.OrdinalIgnoreCase)) return "Musket";
        if (id.StartsWith("DB", StringComparison.OrdinalIgnoreCase)) return "Dual Blades";
        if (id.StartsWith("Bow", StringComparison.OrdinalIgnoreCase) || id.StartsWith("BW", StringComparison.OrdinalIgnoreCase)) return "Bow";
        if (id.StartsWith("KA", StringComparison.OrdinalIgnoreCase)) return "Katana";
        if (id.StartsWith("PL", StringComparison.OrdinalIgnoreCase)) return "Lantern";
        if (id.StartsWith("Drill", StringComparison.OrdinalIgnoreCase) || id.StartsWith("DRILL", StringComparison.OrdinalIgnoreCase)) return "Turbo Lance";
        return "Other";
    }

    static string ArmorType(string id)
    {
        if (id.StartsWith("HA", StringComparison.OrdinalIgnoreCase)) return "Heavy";
        if (id.StartsWith("CA", StringComparison.OrdinalIgnoreCase)) return "Cloth";
        if (id.StartsWith("NA", StringComparison.OrdinalIgnoreCase)) return "Light";
        return "Other";
    }

    static string TreasureType(string id, string manual)
    {
        if (id.IndexOf("Curse", StringComparison.OrdinalIgnoreCase) >= 0
            || manual.StartsWith("CU", StringComparison.OrdinalIgnoreCase))
            return "Cursed";
        if (id.IndexOf("_Fake", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("Fake_", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Knockoff";
        if (id.IndexOf("Common_Red", StringComparison.OrdinalIgnoreCase) >= 0) return "Red";
        if (id.IndexOf("Common_Green", StringComparison.OrdinalIgnoreCase) >= 0) return "Green";
        if (id.IndexOf("Common_Blue", StringComparison.OrdinalIgnoreCase) >= 0) return "Blue";
        if (id.IndexOf("Common_White", StringComparison.OrdinalIgnoreCase) >= 0) return "Chaos";

        var letter = ManualLetter(manual);
        return letter switch
        {
            "A" => "Red",
            "P" => "Green",
            "N" => "Blue",
            "C" => "Chaos",
            "CU" => "Cursed",
            _ => "Other"
        };
    }

    static string ManualLetter(string manual)
    {
        if (string.IsNullOrEmpty(manual)) return "";
        var m = ManualTier.Match(manual);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        var i = 0;
        while (i < manual.Length && char.IsLetter(manual[i])) i++;
        return i > 0 ? manual.Substring(0, i).ToUpperInvariant() : "";
    }

    static void ParseRank(string manual, out int rank, out string quality)
    {
        rank = 0;
        quality = "Original";
        if (string.IsNullOrEmpty(manual)) return;

        if (manual.StartsWith("CU", StringComparison.OrdinalIgnoreCase))
        {
            rank = 4;
            quality = "Epic";
            return;
        }

        var m = ManualTier.Match(manual);
        if (!m.Success || !int.TryParse(m.Groups[2].Value, out var tier))
            return;
        rank = Math.Clamp(tier, 0, 9);
        quality = rank < Qualities.Length ? Qualities[rank] : "Tier " + rank;
    }
}
