using System.Collections.Generic;
using UnityEngine;

namespace Otuken
{
    public enum KitId { Tank, Wolf, Archer, Healer }

    /// <summary>Shared rules for one hero archetype. Distances are in metres, times in seconds.</summary>
    public sealed class Kit
    {
        public KitId Id;
        public string Role;
        public float Hp, Speed, Radius, SkillCd, Aura;
        public bool Melee;
        public float FireCd, Damage, Range, Arc, ProjSpeed, ProjRadius;
        public string WeaponText, SkillText, UltText;
    }

    public sealed class Hero
    {
        public string Key, Name, Title, Weapon, Skill, Ult, Lore, Mark;
        public int Side;
        public KitId Kit;
        public Color Color;
    }

    public struct BotSkill
    {
        public float Mul, Jitter, React;
        public BotSkill(float mul, float jitter, float react) { Mul = mul; Jitter = jitter; React = react; }
    }

    public static class HeroDb
    {
        public static readonly Color[] TeamColor = { new Color(0.37f, 0.69f, 1f), new Color(1f, 0.36f, 0.28f) };
        public static readonly string[] TeamName = { "Tengri's Host", "Erlik's Horde" };
        public static readonly string[] TeamTagline = { "Heroes of the Blue Sky", "Spirits of the underworld" };

        public static readonly string[] DifficultyName = { "Easy", "Normal", "Hard" };
        public static readonly BotSkill[] Difficulty =
        {
            new BotSkill(0.55f, 0.26f, 0.7f),
            new BotSkill(0.80f, 0.14f, 0.4f),
            new BotSkill(1.00f, 0.06f, 0.2f),
        };
        public static readonly BotSkill Ally = new BotSkill(0.8f, 0.13f, 0.35f);

        public static readonly Kit[] Kits =
        {
            new Kit { Id = KitId.Tank, Role = "Tank", Hp = 420, Speed = 1.5f, Radius = 0.32f, SkillCd = 9,
                Melee = true, FireCd = 0.55f, Damage = 42, Range = 0.7f, Arc = 1.7f,
                WeaponText = "Wide melee sweep, 42 damage.",
                SkillText = "Raise a guard for 3 s. Blocks 85% of damage from the front.",
                UltText = "Stun and knock back every enemy close by for 1.6 s, 70 damage." },
            new Kit { Id = KitId.Wolf, Role = "Damage", Hp = 210, Speed = 2.1f, Radius = 0.25f, SkillCd = 6,
                Melee = true, FireCd = 0.28f, Damage = 22, Range = 0.5f, Arc = 1.4f,
                WeaponText = "Fast claw strikes, 22 damage.",
                SkillText = "Leap forward. 55 damage to everyone in the path.",
                UltText = "Call three spirit wolves that hunt for 7 s." },
            new Kit { Id = KitId.Archer, Role = "Damage", Hp = 180, Speed = 1.75f, Radius = 0.24f, SkillCd = 7,
                FireCd = 0.42f, Damage = 30, Range = 6.4f, ProjSpeed = 9.5f, ProjRadius = 0.08f,
                WeaponText = "Long-range shots, 30 damage.",
                SkillText = "Loose a fan of five shots.",
                UltText = "One shot that crosses the whole field through stone and bodies, 170 damage." },
            new Kit { Id = KitId.Healer, Role = "Support", Hp = 200, Speed = 1.82f, Radius = 0.24f, SkillCd = 8, Aura = 13,
                FireCd = 0.3f, Damage = 15, Range = 4.3f, ProjSpeed = 7.2f, ProjRadius = 0.1f,
                WeaponText = "Light bolts, 15 damage. Nearby allies heal over time.",
                SkillText = "Heal every ally around you for 90.",
                UltText = "Fully heal allies around you and make them untouchable for 3 s." },
        };

        static Color Hex(int h) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f);

        public static readonly Hero[] Heroes =
        {
            new Hero { Key = "alp", Name = "Alp Er Tunga", Side = 0, Kit = KitId.Tank, Color = Hex(0xE8B54A), Mark = "helm",
                Title = "King of the Saka", Weapon = "Khagan's Blade", Skill = "Felt Shield", Ult = "Roar of Turan",
                Lore = "The Saka king of the old laments, mourned in the Divanü Lügati't-Türk. He holds the line so the clans can ride." },
            new Hero { Key = "asena", Name = "Asena", Side = 0, Kit = KitId.Wolf, Color = Hex(0xC3CCD6), Mark = "",
                Title = "The Gray She-Wolf", Weapon = "Gray Fangs", Skill = "Pounce", Ult = "Bozkurt Pack",
                Lore = "She nursed the last child of a slaughtered tribe. Her sons became the Ashina, the khans of the Göktürks." },
            new Hero { Key = "mergen", Name = "Mergen", Side = 0, Kit = KitId.Archer, Color = Hex(0x9BE37A), Mark = "",
                Title = "God of the Hunt", Weapon = "Hunter's Bow", Skill = "Five Feathers", Ult = "Arrow of Tengri",
                Lore = "Altai god of the hunt and of sound judgment. His arrow never misses what it was meant for." },
            new Hero { Key = "umay", Name = "Umay", Side = 0, Kit = KitId.Healer, Color = Hex(0xF6E6B8), Mark = "",
                Title = "Mother of the Hearth", Weapon = "Hearth Light", Skill = "Mother's Blessing", Ult = "Umay's Wings",
                Lore = "Goddess who guards mothers and newborns. Where she spreads her wings the hearth fire stays lit." },
            new Hero { Key = "tepegoz", Name = "Tepegöz", Side = 1, Kit = KitId.Tank, Color = Hex(0xB5834F), Mark = "eye",
                Title = "The One-Eyed Giant", Weapon = "Shepherd's Club", Skill = "Stone Hide", Ult = "Earthshaker",
                Lore = "Giant of the Book of Dede Korkut, born to a shepherd and a fairy. Only Basat ever found his blind side." },
            new Hero { Key = "albasti", Name = "Albastı", Side = 1, Kit = KitId.Wolf, Color = Hex(0xFF9A6A), Mark = "hair",
                Title = "The Red Night Spirit", Weapon = "Night Claws", Skill = "Shadow Leap", Ult = "Hounds of the Dark",
                Lore = "A red-haired spirit that comes in the night. Fast and cruel, and still afraid of cold iron." },
            new Hero { Key = "sulmus", Name = "Şulmus", Side = 1, Kit = KitId.Archer, Color = Hex(0xB98AFF), Mark = "horns",
                Title = "Erlik's Tempters", Weapon = "Curse Bolts", Skill = "Hex Fan", Ult = "Black Comet",
                Lore = "Demons Erlik sends up from below to lead people off the true path." },
            new Hero { Key = "erlik", Name = "Erlik", Side = 1, Kit = KitId.Healer, Color = Hex(0xE0566E), Mark = "horns",
                Title = "Lord of Tamag", Weapon = "Tamag Fire", Skill = "Dark Mending", Ult = "Throne of Tamag",
                Lore = "Brother and rival of Ülgen, ruler of the underworld. He keeps the dead and mends his own with black fire." },
        };

        public static Kit KitOf(Hero h) => Kits[(int)h.Kit];

        public static List<Hero> Side(int side)
        {
            var list = new List<Hero>(4);
            foreach (var h in Heroes) if (h.Side == side) list.Add(h);
            return list;
        }
    }
}
