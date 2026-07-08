using Project.Scripts;
using UnityEngine;

public static class BiomeLibrary
{
    private static Color C(int r, int g, int b)
    {
        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    private static WorldData.Biome B(
        string name,

        float temperature,
        float temperatureVariance,

        float moisture,
        float moistureVariance,

        float height,
        float heightVariance,

        float heightOffset,
        float heightMultiplier,
        float erosionStrength,
        float mountainStrength,
        float valleyStrength,
        float roughnessStrength,

        float hillStrength,
        float hillScale,
        float bumpStrength,
        float bumpScale,
        float cliffChance,
        float cliffStrength,
        float cliffScale,

        float volcanoChance,
        float craterChance,
        float mesaChance,

        float townChance,

        Color groundColor,
        Color pathColor,
        Color waterColor,
        Color cliffColor,
        Color beachColor
    )
    {
        return new WorldData.Biome
        {
            name = name,

            temperature = temperature,
            temperatureVariance = temperatureVariance,

            moisture = moisture,
            moistureVariance = moistureVariance,

            height = height,
            heightVariance = heightVariance,

            heightOffset = heightOffset,
            heightMultiplier = heightMultiplier,
            erosionStrength = erosionStrength,
            mountainStrength = mountainStrength,
            valleyStrength = valleyStrength,
            roughnessStrength = roughnessStrength,

            hillStrength = hillStrength,
            hillScale = hillScale,
            bumpStrength = bumpStrength,
            bumpScale = bumpScale,
            cliffChance = cliffChance,
            cliffStrength = cliffStrength,
            cliffScale = cliffScale,

            volcanoChance = volcanoChance,
            craterChance = craterChance,
            mesaChance = mesaChance,

            townChance = townChance,

            groundColor = groundColor,
            pathColor = pathColor,
            waterColor = waterColor,
            cliffColor = cliffColor,
            beachColor = beachColor
        };
    }

    public static readonly WorldData.Biome[] Biomes =
    {
        // --------------------------------------------------------------------
        // Cold Biomes
        // --------------------------------------------------------------------

        B(
            "Frozen Tundra",
            0.08f, 0.08f,
            0.30f, 0.18f,
            0.32f, 0.20f,

            -0.04f,
            0.78f,
            1.35f,
            0.35f,
            0.65f,
            0.55f,

            // Micro terrain: hills, bumps, cliffs
            0.025f, // hillStrength
            46f, // hillScale
            0.010f, // bumpStrength
            18f, // bumpScale
            0.030f, // cliffChance
            0.020f, // cliffStrength
            28f, // cliffScale

            0.00f,
            0.02f,
            0.00f,

            0.10f,

            C(175, 205, 205),
            C(210, 215, 205),
            C(80, 150, 210),
            C(145, 165, 170),
            C(190, 185, 160)
        ),

        B(
            "Glacier Fields",
            0.03f, 0.05f,
            0.45f, 0.18f,
            0.38f, 0.22f,

            0.02f,
            0.90f,
            1.55f,
            0.55f,
            0.45f,
            0.35f,

            // Micro terrain: hills, bumps, cliffs
            0.015f, // hillStrength
            58f, // hillScale
            0.008f, // bumpStrength
            22f, // bumpScale
            0.080f, // cliffChance
            0.035f, // cliffStrength
            30f, // cliffScale

            0.00f,
            0.03f,
            0.00f,

            0.00f,

            C(210, 235, 245),
            C(235, 245, 250),
            C(70, 170, 230),
            C(170, 205, 220),
            C(220, 220, 205)
        ),

        B(
            "Snowy Mountains",
            0.10f, 0.10f,
            0.40f, 0.18f,
            0.82f, 0.16f,

            0.12f,
            1.32f,
            0.70f,
            1.65f,
            1.35f,
            1.15f,

            // Micro terrain: hills, bumps, cliffs
            0.020f, // hillStrength
            34f, // hillScale
            0.020f, // bumpStrength
            10f, // bumpScale
            0.420f, // cliffChance
            0.110f, // cliffStrength
            14f, // cliffScale

            0.00f,
            0.04f,
            0.00f,

            0.03f,

            C(220, 225, 225),
            C(160, 160, 155),
            C(60, 135, 210),
            C(135, 140, 145),
            C(190, 185, 170)
        ),

        B(
            "Boreal Forest",
            0.22f, 0.12f,
            0.58f, 0.18f,
            0.46f, 0.20f,

            -0.01f,
            0.95f,
            1.10f,
            0.70f,
            0.90f,
            0.75f,

            // Micro terrain: hills, bumps, cliffs
            0.050f, // hillStrength
            38f, // hillScale
            0.018f, // bumpStrength
            13f, // bumpScale
            0.090f, // cliffChance
            0.035f, // cliffStrength
            22f, // cliffScale

            0.00f,
            0.02f,
            0.00f,

            0.22f,

            C(45, 105, 75),
            C(110, 95, 70),
            C(45, 120, 190),
            C(85, 100, 90),
            C(180, 170, 120)
        ),

        B(
            "Frostfire Peaks",
            0.18f, 0.10f,
            0.28f, 0.14f,
            0.90f, 0.10f,

            0.16f,
            1.45f,
            0.55f,
            1.90f,
            1.20f,
            1.55f,

            // Micro terrain: hills, bumps, cliffs
            0.025f, // hillStrength
            30f, // hillScale
            0.030f, // bumpStrength
            8f, // bumpScale
            0.500f, // cliffChance
            0.130f, // cliffStrength
            12f, // cliffScale

            0.35f,
            0.08f,
            0.00f,

            0.01f,

            C(85, 90, 105),
            C(130, 135, 145),
            C(80, 190, 230),
            C(70, 75, 90),
            C(190, 175, 155)
        ),

        // --------------------------------------------------------------------
        // Temperate Biomes
        // --------------------------------------------------------------------

        B(
            "Grasslands",
            0.55f, 0.16f,
            0.48f, 0.20f,
            0.38f, 0.20f,

            -0.03f,
            0.72f,
            1.25f,
            0.25f,
            0.65f,
            0.35f,

            // Micro terrain: hills, bumps, cliffs
            0.035f, // hillStrength
            42f, // hillScale
            0.012f, // bumpStrength
            16f, // bumpScale
            0.040f, // cliffChance
            0.025f, // cliffStrength
            22f, // cliffScale

            0.00f,
            0.01f,
            0.00f,

            0.85f,

            C(90, 175, 75),
            C(180, 145, 85),
            C(45, 125, 220),
            C(100, 130, 90),
            C(220, 195, 120)
        ),

        B(
            "Wildflower Meadows",
            0.58f, 0.14f,
            0.55f, 0.16f,
            0.36f, 0.18f,

            -0.04f,
            0.68f,
            1.35f,
            0.18f,
            0.55f,
            0.30f,

            // Micro terrain: hills, bumps, cliffs
            0.030f, // hillStrength
            46f, // hillScale
            0.010f, // bumpStrength
            18f, // bumpScale
            0.030f, // cliffChance
            0.020f, // cliffStrength
            24f, // cliffScale

            0.00f,
            0.01f,
            0.00f,

            0.75f,

            C(120, 190, 85),
            C(195, 160, 100),
            C(60, 145, 220),
            C(105, 135, 95),
            C(225, 205, 135)
        ),

        B(
            "Temperate Forest",
            0.50f, 0.14f,
            0.68f, 0.16f,
            0.46f, 0.20f,

            -0.01f,
            0.90f,
            1.05f,
            0.65f,
            0.85f,
            0.70f,

            // Micro terrain: hills, bumps, cliffs
            0.050f, // hillStrength
            36f, // hillScale
            0.020f, // bumpStrength
            13f, // bumpScale
            0.080f, // cliffChance
            0.035f, // cliffStrength
            20f, // cliffScale

            0.00f,
            0.01f,
            0.00f,

            0.35f,

            C(45, 135, 55),
            C(120, 90, 55),
            C(45, 110, 185),
            C(80, 105, 80),
            C(195, 175, 115)
        ),

        B(
            "Ancient Woodland",
            0.48f, 0.12f,
            0.72f, 0.14f,
            0.52f, 0.18f,

            0.01f,
            0.96f,
            1.15f,
            0.75f,
            0.95f,
            0.85f,

            // Micro terrain: hills, bumps, cliffs
            0.060f, // hillStrength
            34f, // hillScale
            0.024f, // bumpStrength
            11f, // bumpScale
            0.120f, // cliffChance
            0.045f, // cliffStrength
            18f, // cliffScale

            0.00f,
            0.02f,
            0.00f,

            0.18f,

            C(30, 95, 45),
            C(85, 65, 45),
            C(35, 95, 150),
            C(65, 85, 70),
            C(165, 150, 100)
        ),

        B(
            "Rolling Hills",
            0.52f, 0.16f,
            0.45f, 0.18f,
            0.58f, 0.18f,

            0.03f,
            0.98f,
            0.95f,
            0.85f,
            0.80f,
            0.70f,

            // Micro terrain: hills, bumps, cliffs
            0.090f, // hillStrength
            34f, // hillScale
            0.022f, // bumpStrength
            12f, // bumpScale
            0.100f, // cliffChance
            0.040f, // cliffStrength
            20f, // cliffScale

            0.00f,
            0.02f,
            0.00f,

            0.60f,

            C(105, 165, 75),
            C(145, 115, 70),
            C(50, 125, 200),
            C(100, 120, 85),
            C(205, 185, 125)
        ),

        B(
            "Highland Moors",
            0.38f, 0.14f,
            0.62f, 0.20f,
            0.66f, 0.18f,

            0.06f,
            1.10f,
            0.90f,
            1.05f,
            1.15f,
            0.95f,

            // Micro terrain: hills, bumps, cliffs
            0.075f, // hillStrength
            32f, // hillScale
            0.025f, // bumpStrength
            10f, // bumpScale
            0.160f, // cliffChance
            0.055f, // cliffStrength
            18f, // cliffScale

            0.00f,
            0.03f,
            0.00f,

            0.18f,

            C(90, 105, 70),
            C(100, 85, 65),
            C(55, 105, 165),
            C(90, 95, 85),
            C(170, 160, 120)
        ),

        // --------------------------------------------------------------------
        // Hot Dry Biomes
        // --------------------------------------------------------------------

        B(
            "Desert",
            0.88f, 0.10f,
            0.10f, 0.08f,
            0.30f, 0.24f,

            -0.03f,
            0.70f,
            1.45f,
            0.20f,
            0.35f,
            0.45f,

            // Micro terrain: hills, bumps, cliffs
            0.020f, // hillStrength
            54f, // hillScale
            0.010f, // bumpStrength
            20f, // bumpScale
            0.030f, // cliffChance
            0.020f, // cliffStrength
            30f, // cliffScale

            0.00f,
            0.03f,
            0.25f,

            0.18f,

            C(210, 175, 95),
            C(185, 140, 70),
            C(40, 135, 210),
            C(145, 120, 85),
            C(235, 210, 130)
        ),

        B(
            "Dune Sea",
            0.92f, 0.08f,
            0.06f, 0.06f,
            0.34f, 0.22f,

            -0.02f,
            0.62f,
            1.65f,
            0.10f,
            0.25f,
            0.75f,

            // Micro terrain: hills, bumps, cliffs
            0.035f, // hillStrength
            42f, // hillScale
            0.012f, // bumpStrength
            18f, // bumpScale
            0.010f, // cliffChance
            0.010f, // cliffStrength
            36f, // cliffScale

            0.00f,
            0.02f,
            0.10f,

            0.04f,

            C(225, 190, 105),
            C(200, 155, 75),
            C(35, 125, 205),
            C(160, 130, 85),
            C(245, 220, 145)
        ),

        B(
            "Badlands",
            0.76f, 0.14f,
            0.18f, 0.10f,
            0.52f, 0.22f,

            0.04f,
            0.95f,
            1.20f,
            0.75f,
            1.40f,
            1.15f,

            // Micro terrain: hills, bumps, cliffs
            0.070f, // hillStrength
            28f, // hillScale
            0.035f, // bumpStrength
            8f, // bumpScale
            0.360f, // cliffChance
            0.090f, // cliffStrength
            13f, // cliffScale

            0.02f,
            0.08f,
            0.65f,

            0.08f,

            C(160, 85, 55),
            C(130, 70, 45),
            C(45, 120, 185),
            C(125, 70, 55),
            C(210, 150, 85)
        ),

        B(
            "Rocky Steppe",
            0.62f, 0.16f,
            0.28f, 0.12f,
            0.48f, 0.22f,

            0.02f,
            0.95f,
            1.05f,
            0.75f,
            0.85f,
            1.00f,

            // Micro terrain: hills, bumps, cliffs
            0.075f, // hillStrength
            30f, // hillScale
            0.035f, // bumpStrength
            8f, // bumpScale
            0.280f, // cliffChance
            0.075f, // cliffStrength
            14f, // cliffScale

            0.00f,
            0.04f,
            0.20f,

            0.22f,

            C(135, 140, 80),
            C(120, 100, 70),
            C(55, 125, 190),
            C(110, 105, 85),
            C(200, 175, 105)
        ),

        B(
            "Oasis",
            0.84f, 0.10f,
            0.42f, 0.12f,
            0.28f, 0.16f,

            -0.06f,
            0.60f,
            1.30f,
            0.15f,
            0.35f,
            0.30f,

            // Micro terrain: hills, bumps, cliffs
            0.025f, // hillStrength
            44f, // hillScale
            0.010f, // bumpStrength
            18f, // bumpScale
            0.020f, // cliffChance
            0.015f, // cliffStrength
            28f, // cliffScale

            0.00f,
            0.01f,
            0.00f,

            0.70f,

            C(75, 165, 85),
            C(190, 150, 75),
            C(40, 170, 220),
            C(120, 120, 80),
            C(235, 205, 125)
        ),

        B(
            "Glass Desert",
            0.95f, 0.06f,
            0.04f, 0.05f,
            0.36f, 0.16f,

            0.00f,
            0.78f,
            1.85f,
            0.25f,
            0.25f,
            0.95f,

            // Micro terrain: hills, bumps, cliffs
            0.025f, // hillStrength
            50f, // hillScale
            0.018f, // bumpStrength
            12f, // bumpScale
            0.120f, // cliffChance
            0.050f, // cliffStrength
            18f, // cliffScale

            0.04f,
            0.12f,
            0.18f,

            0.02f,

            C(215, 225, 215),
            C(190, 190, 175),
            C(55, 180, 220),
            C(165, 170, 165),
            C(245, 235, 200)
        ),

        // --------------------------------------------------------------------
        // Hot Wet Biomes
        // --------------------------------------------------------------------

        B(
            "Tropical Rainforest",
            0.78f, 0.12f,
            0.86f, 0.10f,
            0.42f, 0.20f,

            -0.01f,
            0.88f,
            1.25f,
            0.55f,
            1.15f,
            0.90f,

            // Micro terrain: hills, bumps, cliffs
            0.055f, // hillStrength
            34f, // hillScale
            0.025f, // bumpStrength
            10f, // bumpScale
            0.080f, // cliffChance
            0.035f, // cliffStrength
            18f, // cliffScale

            0.00f,
            0.02f,
            0.00f,

            0.16f,

            C(25, 125, 45),
            C(95, 65, 40),
            C(30, 130, 160),
            C(65, 90, 65),
            C(210, 185, 110)
        ),

        B(
            "Jungle Basin",
            0.82f, 0.10f,
            0.92f, 0.08f,
            0.24f, 0.14f,

            -0.08f,
            0.58f,
            1.50f,
            0.20f,
            0.75f,
            0.70f,

            // Micro terrain: hills, bumps, cliffs
            0.030f, // hillStrength
            46f, // hillScale
            0.018f, // bumpStrength
            12f, // bumpScale
            0.040f, // cliffChance
            0.025f, // cliffStrength
            22f, // cliffScale

            0.00f,
            0.02f,
            0.00f,

            0.08f,

            C(20, 100, 40),
            C(70, 55, 35),
            C(35, 115, 130),
            C(55, 75, 55),
            C(175, 155, 95)
        ),

        B(
            "Mangrove Coast",
            0.74f, 0.12f,
            0.88f, 0.10f,
            0.16f, 0.12f,

            -0.10f,
            0.45f,
            1.75f,
            0.08f,
            0.35f,
            0.30f,

            // Micro terrain: hills, bumps, cliffs
            0.012f, // hillStrength
            58f, // hillScale
            0.006f, // bumpStrength
            22f, // bumpScale
            0.010f, // cliffChance
            0.010f, // cliffStrength
            32f, // cliffScale

            0.00f,
            0.01f,
            0.00f,

            0.12f,

            C(45, 110, 65),
            C(80, 65, 45),
            C(35, 105, 115),
            C(65, 80, 70),
            C(185, 165, 105)
        ),

        B(
            "Swamp",
            0.62f, 0.14f,
            0.88f, 0.10f,
            0.18f, 0.14f,

            -0.09f,
            0.50f,
            1.65f,
            0.08f,
            0.45f,
            0.35f,

            // Micro terrain: hills, bumps, cliffs
            0.015f, // hillStrength
            55f, // hillScale
            0.006f, // bumpStrength
            20f, // bumpScale
            0.010f, // cliffChance
            0.010f, // cliffStrength
            30f, // cliffScale

            0.00f,
            0.01f,
            0.00f,

            0.12f,

            C(55, 95, 45),
            C(85, 70, 45),
            C(45, 80, 70),
            C(70, 80, 65),
            C(130, 120, 75)
        ),

        B(
            "Poison Fen",
            0.58f, 0.12f,
            0.96f, 0.06f,
            0.14f, 0.10f,

            -0.11f,
            0.45f,
            1.80f,
            0.06f,
            0.40f,
            0.45f,

            // Micro terrain: hills, bumps, cliffs
            0.012f, // hillStrength
            60f, // hillScale
            0.008f, // bumpStrength
            18f, // bumpScale
            0.015f, // cliffChance
            0.012f, // cliffStrength
            28f, // cliffScale

            0.00f,
            0.03f,
            0.00f,

            0.03f,

            C(65, 85, 35),
            C(75, 60, 40),
            C(85, 140, 45),
            C(65, 75, 50),
            C(125, 130, 75)
        ),

        // --------------------------------------------------------------------
        // Water and Coast Biomes
        // --------------------------------------------------------------------

        B(
            "Sandy Coast",
            0.62f, 0.22f,
            0.58f, 0.24f,
            0.10f, 0.10f,

            -0.06f,
            0.42f,
            1.60f,
            0.05f,
            0.20f,
            0.20f,

            // Micro terrain: hills, bumps, cliffs
            0.015f, // hillStrength
            52f, // hillScale
            0.008f, // bumpStrength
            20f, // bumpScale
            0.020f, // cliffChance
            0.015f, // cliffStrength
            28f, // cliffScale

            0.00f,
            0.01f,
            0.00f,

            0.35f,

            C(185, 175, 100),
            C(160, 135, 80),
            C(45, 135, 220),
            C(135, 130, 105),
            C(235, 215, 145)
        ),

        B(
            "Rocky Coast",
            0.46f, 0.20f,
            0.62f, 0.22f,
            0.18f, 0.12f,

            -0.03f,
            0.62f,
            1.20f,
            0.40f,
            0.35f,
            0.90f,

            // Micro terrain: hills, bumps, cliffs
            0.045f, // hillStrength
            30f, // hillScale
            0.024f, // bumpStrength
            9f, // bumpScale
            0.260f, // cliffChance
            0.075f, // cliffStrength
            14f, // cliffScale

            0.00f,
            0.03f,
            0.00f,

            0.22f,

            C(105, 110, 100),
            C(90, 85, 75),
            C(40, 105, 185),
            C(90, 90, 85),
            C(175, 165, 130)
        ),

        B(
            "Coral Reef",
            0.74f, 0.12f,
            0.92f, 0.08f,
            0.04f, 0.06f,

            -0.08f,
            0.25f,
            1.25f,
            0.02f,
            0.05f,
            0.25f,

            // Micro terrain: hills, bumps, cliffs
            0.004f, // hillStrength
            80f, // hillScale
            0.004f, // bumpStrength
            30f, // bumpScale
            0.00f, // cliffChance
            0.00f, // cliffStrength
            40f, // cliffScale

            0.00f,
            0.00f,
            0.00f,

            0.00f,

            C(95, 190, 160),
            C(205, 175, 120),
            C(35, 185, 220),
            C(120, 165, 150),
            C(245, 225, 165)
        ),

        B(
            "Kelp Forest",
            0.42f, 0.18f,
            0.95f, 0.05f,
            0.02f, 0.06f,

            -0.10f,
            0.22f,
            1.30f,
            0.02f,
            0.05f,
            0.35f,

            // Micro terrain: hills, bumps, cliffs
            0.004f, // hillStrength
            80f, // hillScale
            0.004f, // bumpStrength
            30f, // bumpScale
            0.00f, // cliffChance
            0.00f, // cliffStrength
            40f, // cliffScale

            0.00f,
            0.00f,
            0.00f,

            0.00f,

            C(35, 105, 75),
            C(120, 105, 70),
            C(30, 95, 145),
            C(55, 95, 85),
            C(175, 160, 110)
        ),

        B(
            "Deep Ocean",
            0.40f, 0.35f,
            1.00f, 0.05f,
            0.00f, 0.05f,

            -0.16f,
            0.15f,
            1.10f,
            0.01f,
            0.02f,
            0.20f,

            // Micro terrain: hills, bumps, cliffs
            0.00f, // hillStrength
            80f, // hillScale
            0.002f, // bumpStrength
            36f, // bumpScale
            0.00f, // cliffChance
            0.00f, // cliffStrength
            40f, // cliffScale

            0.00f,
            0.00f,
            0.00f,

            0.00f,

            C(20, 45, 90),
            C(25, 55, 100),
            C(10, 55, 130),
            C(30, 45, 75),
            C(90, 100, 120)
        ),

        // --------------------------------------------------------------------
        // Mountain Biomes
        // --------------------------------------------------------------------

        B(
            "Mountains",
            0.36f, 0.18f,
            0.42f, 0.18f,
            0.78f, 0.16f,

            0.10f,
            1.28f,
            0.75f,
            1.55f,
            1.30f,
            1.20f,

            // Micro terrain: hills, bumps, cliffs
            0.025f, // hillStrength
            30f, // hillScale
            0.030f, // bumpStrength
            8f, // bumpScale
            0.420f, // cliffChance
            0.110f, // cliffStrength
            13f, // cliffScale

            0.02f,
            0.05f,
            0.00f,

            0.04f,

            C(110, 115, 105),
            C(125, 115, 95),
            C(50, 120, 190),
            C(90, 90, 85),
            C(180, 170, 130)
        ),

        B(
            "Alpine Cliffs",
            0.26f, 0.12f,
            0.36f, 0.16f,
            0.88f, 0.10f,

            0.16f,
            1.38f,
            0.55f,
            1.90f,
            1.45f,
            1.35f,

            // Micro terrain: hills, bumps, cliffs
            0.018f, // hillStrength
            28f, // hillScale
            0.032f, // bumpStrength
            7f, // bumpScale
            0.550f, // cliffChance
            0.140f, // cliffStrength
            11f, // cliffScale

            0.00f,
            0.06f,
            0.00f,

            0.01f,

            C(130, 135, 130),
            C(100, 95, 90),
            C(65, 145, 205),
            C(80, 80, 78),
            C(175, 165, 140)
        ),

        B(
            "Crystal Highlands",
            0.44f, 0.14f,
            0.44f, 0.16f,
            0.72f, 0.14f,

            0.09f,
            1.20f,
            0.75f,
            1.35f,
            1.10f,
            1.05f,

            // Micro terrain: hills, bumps, cliffs
            0.045f, // hillStrength
            30f, // hillScale
            0.028f, // bumpStrength
            9f, // bumpScale
            0.280f, // cliffChance
            0.085f, // cliffStrength
            14f, // cliffScale

            0.01f,
            0.08f,
            0.00f,

            0.04f,

            C(125, 160, 185),
            C(145, 145, 170),
            C(80, 180, 230),
            C(105, 130, 150),
            C(205, 205, 180)
        ),

        // --------------------------------------------------------------------
        // Volcanic Biomes
        // --------------------------------------------------------------------

        B(
            "Volcano Slopes",
            0.88f, 0.10f,
            0.18f, 0.10f,
            0.86f, 0.12f,

            0.15f,
            1.35f,
            0.55f,
            1.65f,
            1.05f,
            1.80f,

            // Micro terrain: hills, bumps, cliffs
            0.035f, // hillStrength
            26f, // hillScale
            0.045f, // bumpStrength
            7f, // bumpScale
            0.420f, // cliffChance
            0.120f, // cliffStrength
            12f, // cliffScale

            0.95f,
            0.12f,
            0.00f,

            0.00f,

            C(70, 65, 60),
            C(95, 75, 55),
            C(180, 60, 25),
            C(45, 42, 40),
            C(120, 95, 70)
        ),

        B(
            "Lava Fields",
            1.00f, 0.06f,
            0.04f, 0.04f,
            0.54f, 0.18f,

            0.05f,
            0.95f,
            0.70f,
            1.00f,
            0.55f,
            1.90f,

            // Micro terrain: hills, bumps, cliffs
            0.025f, // hillStrength
            30f, // hillScale
            0.050f, // bumpStrength
            6f, // bumpScale
            0.300f, // cliffChance
            0.090f, // cliffStrength
            12f, // cliffScale

            0.75f,
            0.08f,
            0.00f,

            0.00f,

            C(45, 40, 38),
            C(95, 45, 30),
            C(255, 85, 20),
            C(30, 28, 28),
            C(120, 80, 45)
        ),

        B(
            "Ash Wastes",
            0.72f, 0.16f,
            0.12f, 0.10f,
            0.46f, 0.20f,

            0.00f,
            0.85f,
            1.20f,
            0.65f,
            0.70f,
            1.35f,

            // Micro terrain: hills, bumps, cliffs
            0.045f, // hillStrength
            34f, // hillScale
            0.030f, // bumpStrength
            9f, // bumpScale
            0.180f, // cliffChance
            0.060f, // cliffStrength
            16f, // cliffScale

            0.35f,
            0.12f,
            0.05f,

            0.02f,

            C(95, 90, 85),
            C(70, 65, 60),
            C(75, 80, 90),
            C(65, 62, 60),
            C(135, 125, 105)
        ),

        B(
            "Obsidian Plains",
            0.78f, 0.12f,
            0.08f, 0.08f,
            0.40f, 0.16f,

            0.00f,
            0.82f,
            0.95f,
            0.45f,
            0.45f,
            1.40f,

            // Micro terrain: hills, bumps, cliffs
            0.035f, // hillStrength
            32f, // hillScale
            0.040f, // bumpStrength
            7f, // bumpScale
            0.260f, // cliffChance
            0.085f, // cliffStrength
            12f, // cliffScale

            0.45f,
            0.08f,
            0.00f,

            0.01f,

            C(30, 28, 36),
            C(55, 48, 60),
            C(210, 70, 30),
            C(22, 20, 26),
            C(100, 85, 75)
        ),

        B(
            "Sulfur Springs",
            0.82f, 0.10f,
            0.48f, 0.14f,
            0.36f, 0.16f,

            -0.02f,
            0.72f,
            1.45f,
            0.35f,
            0.50f,
            0.95f,

            // Micro terrain: hills, bumps, cliffs
            0.030f, // hillStrength
            40f, // hillScale
            0.020f, // bumpStrength
            10f, // bumpScale
            0.080f, // cliffChance
            0.035f, // cliffStrength
            20f, // cliffScale

            0.25f,
            0.04f,
            0.00f,

            0.06f,

            C(150, 145, 70),
            C(130, 110, 55),
            C(160, 190, 60),
            C(100, 100, 65),
            C(190, 175, 90)
        ),

        // --------------------------------------------------------------------
        // Fantasy Forests
        // --------------------------------------------------------------------

        B(
            "Crystal Forest",
            0.46f, 0.14f,
            0.60f, 0.16f,
            0.44f, 0.18f,

            0.00f,
            0.92f,
            1.00f,
            0.60f,
            0.85f,
            0.95f,

            // Micro terrain: hills, bumps, cliffs
            0.050f, // hillStrength
            34f, // hillScale
            0.030f, // bumpStrength
            8f, // bumpScale
            0.160f, // cliffChance
            0.060f, // cliffStrength
            16f, // cliffScale

            0.00f,
            0.06f,
            0.00f,

            0.12f,

            C(80, 150, 165),
            C(130, 120, 160),
            C(95, 210, 235),
            C(80, 120, 145),
            C(205, 205, 220)
        ),

        B(
            "Giant Mushroom Grove",
            0.58f, 0.12f,
            0.82f, 0.10f,
            0.36f, 0.16f,

            -0.03f,
            0.75f,
            1.45f,
            0.30f,
            0.55f,
            0.80f,

            // Micro terrain: hills, bumps, cliffs
            0.035f, // hillStrength
            42f, // hillScale
            0.020f, // bumpStrength
            11f, // bumpScale
            0.040f, // cliffChance
            0.025f, // cliffStrength
            24f, // cliffScale

            0.00f,
            0.03f,
            0.00f,

            0.10f,

            C(85, 75, 105),
            C(115, 80, 75),
            C(85, 120, 180),
            C(75, 65, 90),
            C(190, 160, 140)
        ),

        B(
            "Fey Wildwood",
            0.54f, 0.12f,
            0.74f, 0.12f,
            0.42f, 0.18f,

            -0.01f,
            0.88f,
            1.10f,
            0.50f,
            0.80f,
            0.75f,

            // Micro terrain: hills, bumps, cliffs
            0.045f, // hillStrength
            38f, // hillScale
            0.018f, // bumpStrength
            13f, // bumpScale
            0.060f, // cliffChance
            0.030f, // cliffStrength
            22f, // cliffScale

            0.00f,
            0.05f,
            0.00f,

            0.18f,

            C(45, 155, 95),
            C(150, 110, 165),
            C(90, 190, 210),
            C(70, 110, 90),
            C(215, 190, 145)
        ),

        B(
            "Bloodleaf Woods",
            0.56f, 0.12f,
            0.56f, 0.16f,
            0.44f, 0.18f,

            0.00f,
            0.90f,
            1.05f,
            0.55f,
            0.80f,
            0.90f,

            // Micro terrain: hills, bumps, cliffs
            0.050f, // hillStrength
            36f, // hillScale
            0.024f, // bumpStrength
            10f, // bumpScale
            0.100f, // cliffChance
            0.040f, // cliffStrength
            18f, // cliffScale

            0.00f,
            0.04f,
            0.00f,

            0.12f,

            C(110, 35, 45),
            C(85, 55, 45),
            C(75, 90, 130),
            C(80, 45, 55),
            C(170, 130, 100)
        ),

        B(
            "Moonlit Grove",
            0.38f, 0.12f,
            0.68f, 0.14f,
            0.40f, 0.18f,

            -0.01f,
            0.86f,
            1.15f,
            0.45f,
            0.75f,
            0.70f,

            // Micro terrain: hills, bumps, cliffs
            0.040f, // hillStrength
            42f, // hillScale
            0.018f, // bumpStrength
            14f, // bumpScale
            0.050f, // cliffChance
            0.025f, // cliffStrength
            24f, // cliffScale

            0.00f,
            0.04f,
            0.00f,

            0.10f,

            C(55, 80, 115),
            C(110, 105, 140),
            C(90, 135, 210),
            C(70, 80, 110),
            C(185, 180, 205)
        ),

        // --------------------------------------------------------------------
        // Magical and Exotic Biomes
        // --------------------------------------------------------------------

        B(
            "Mana Springs",
            0.50f, 0.14f,
            0.76f, 0.12f,
            0.34f, 0.16f,

            -0.04f,
            0.70f,
            1.30f,
            0.25f,
            0.50f,
            0.55f,

            // Micro terrain: hills, bumps, cliffs
            0.030f, // hillStrength
            44f, // hillScale
            0.014f, // bumpStrength
            16f, // bumpScale
            0.040f, // cliffChance
            0.020f, // cliffStrength
            24f, // cliffScale

            0.00f,
            0.04f,
            0.00f,

            0.30f,

            C(70, 120, 150),
            C(120, 105, 160),
            C(95, 95, 255),
            C(85, 100, 135),
            C(190, 180, 220)
        ),

        B(
            "Arcane Highlands",
            0.42f, 0.14f,
            0.46f, 0.16f,
            0.68f, 0.16f,

            0.08f,
            1.15f,
            0.85f,
            1.20f,
            1.05f,
            1.15f,

            // Micro terrain: hills, bumps, cliffs
            0.065f, // hillStrength
            30f, // hillScale
            0.030f, // bumpStrength
            9f, // bumpScale
            0.240f, // cliffChance
            0.075f, // cliffStrength
            15f, // cliffScale

            0.02f,
            0.08f,
            0.00f,

            0.08f,

            C(95, 80, 145),
            C(125, 105, 160),
            C(80, 150, 230),
            C(80, 70, 120),
            C(195, 180, 215)
        ),

        B(
            "Floating Isles",
            0.48f, 0.18f,
            0.52f, 0.18f,
            0.94f, 0.06f,

            0.22f,
            1.45f,
            0.40f,
            1.80f,
            0.75f,
            1.10f,

            // Micro terrain: hills, bumps, cliffs
            0.020f, // hillStrength
            26f, // hillScale
            0.028f, // bumpStrength
            8f, // bumpScale
            0.360f, // cliffChance
            0.100f, // cliffStrength
            13f, // cliffScale

            0.00f,
            0.10f,
            0.00f,

            0.03f,

            C(115, 175, 105),
            C(150, 130, 95),
            C(100, 175, 235),
            C(95, 120, 95),
            C(215, 205, 160)
        ),

        B(
            "Starfall Crater",
            0.34f, 0.16f,
            0.22f, 0.14f,
            0.24f, 0.12f,

            -0.08f,
            0.65f,
            1.25f,
            0.35f,
            0.65f,
            1.40f,

            // Micro terrain: hills, bumps, cliffs
            0.020f, // hillStrength
            46f, // hillScale
            0.022f, // bumpStrength
            10f, // bumpScale
            0.180f, // cliffChance
            0.070f, // cliffStrength
            15f, // cliffScale

            0.00f,
            0.95f,
            0.00f,

            0.00f,

            C(55, 55, 85),
            C(100, 95, 125),
            C(90, 120, 220),
            C(45, 45, 70),
            C(160, 155, 190)
        ),

        B(
            "Rune Ruins",
            0.50f, 0.18f,
            0.38f, 0.18f,
            0.50f, 0.18f,

            0.00f,
            0.90f,
            1.05f,
            0.55f,
            0.70f,
            0.75f,

            // Micro terrain: hills, bumps, cliffs
            0.040f, // hillStrength
            38f, // hillScale
            0.022f, // bumpStrength
            11f, // bumpScale
            0.120f, // cliffChance
            0.050f, // cliffStrength
            17f, // cliffScale

            0.00f,
            0.12f,
            0.00f,

            0.25f,

            C(100, 105, 95),
            C(130, 120, 100),
            C(55, 130, 200),
            C(80, 85, 80),
            C(190, 175, 130)
        ),

        B(
            "Thunder Plains",
            0.60f, 0.14f,
            0.50f, 0.16f,
            0.42f, 0.18f,

            -0.01f,
            0.82f,
            1.10f,
            0.45f,
            0.90f,
            1.20f,

            // Micro terrain: hills, bumps, cliffs
            0.045f, // hillStrength
            38f, // hillScale
            0.020f, // bumpStrength
            12f, // bumpScale
            0.100f, // cliffChance
            0.045f, // cliffStrength
            18f, // cliffScale

            0.00f,
            0.08f,
            0.00f,

            0.35f,

            C(95, 110, 75),
            C(105, 100, 80),
            C(60, 105, 170),
            C(80, 85, 75),
            C(180, 170, 120)
        ),

        B(
            "Shimmering Sands",
            0.82f, 0.10f,
            0.16f, 0.08f,
            0.32f, 0.18f,

            -0.02f,
            0.68f,
            1.60f,
            0.18f,
            0.35f,
            0.65f,

            // Micro terrain: hills, bumps, cliffs
            0.030f, // hillStrength
            44f, // hillScale
            0.018f, // bumpStrength
            14f, // bumpScale
            0.050f, // cliffChance
            0.030f, // cliffStrength
            22f, // cliffScale

            0.00f,
            0.06f,
            0.22f,

            0.08f,

            C(220, 200, 145),
            C(195, 170, 115),
            C(80, 180, 230),
            C(165, 150, 120),
            C(250, 230, 170)
        ),

        B(
            "Dragonbone Wastes",
            0.70f, 0.14f,
            0.14f, 0.10f,
            0.44f, 0.20f,

            0.00f,
            0.88f,
            1.25f,
            0.55f,
            0.75f,
            0.95f,

            // Micro terrain: hills, bumps, cliffs
            0.055f, // hillStrength
            32f, // hillScale
            0.030f, // bumpStrength
            9f, // bumpScale
            0.220f, // cliffChance
            0.070f, // cliffStrength
            15f, // cliffScale

            0.05f,
            0.18f,
            0.35f,

            0.02f,

            C(165, 150, 125),
            C(135, 110, 85),
            C(65, 115, 165),
            C(120, 110, 95),
            C(210, 195, 160)
        ),

        B(
            "Shadowfen",
            0.44f, 0.12f,
            0.90f, 0.08f,
            0.16f, 0.12f,

            -0.10f,
            0.48f,
            1.75f,
            0.06f,
            0.40f,
            0.45f,

            // Micro terrain: hills, bumps, cliffs
            0.012f, // hillStrength
            58f, // hillScale
            0.010f, // bumpStrength
            18f, // bumpScale
            0.020f, // cliffChance
            0.015f, // cliffStrength
            28f, // cliffScale

            0.00f,
            0.05f,
            0.00f,

            0.04f,

            C(35, 45, 50),
            C(60, 55, 60),
            C(35, 55, 85),
            C(45, 50, 60),
            C(115, 110, 120)
        ),

        B(
            "Luminous Caverns",
            0.32f, 0.16f,
            0.70f, 0.14f,
            0.12f, 0.10f,

            -0.12f,
            0.45f,
            1.50f,
            0.20f,
            0.35f,
            0.80f,

            // Micro terrain: hills, bumps, cliffs
            0.020f, // hillStrength
            38f, // hillScale
            0.030f, // bumpStrength
            8f, // bumpScale
            0.220f, // cliffChance
            0.080f, // cliffStrength
            14f, // cliffScale

            0.00f,
            0.08f,
            0.00f,

            0.00f,

            C(45, 65, 80),
            C(75, 80, 100),
            C(70, 190, 210),
            C(40, 55, 70),
            C(150, 160, 175)
        ),

        B(
            "Eldritch Mire",
            0.52f, 0.12f,
            0.98f, 0.04f,
            0.10f, 0.08f,

            -0.14f,
            0.40f,
            1.90f,
            0.04f,
            0.30f,
            0.65f,

            // Micro terrain: hills, bumps, cliffs
            0.012f, // hillStrength
            60f, // hillScale
            0.012f, // bumpStrength
            16f, // bumpScale
            0.020f, // cliffChance
            0.015f, // cliffStrength
            28f, // cliffScale

            0.00f,
            0.10f,
            0.00f,

            0.00f,

            C(45, 55, 40),
            C(80, 65, 65),
            C(85, 45, 115),
            C(50, 50, 45),
            C(120, 110, 95)
        ),

        B(
            "Sunken Necropolis",
            0.46f, 0.14f,
            0.84f, 0.10f,
            0.08f, 0.08f,

            -0.13f,
            0.42f,
            1.60f,
            0.15f,
            0.30f,
            0.70f,

            // Micro terrain: hills, bumps, cliffs
            0.010f, // hillStrength
            58f, // hillScale
            0.016f, // bumpStrength
            14f, // bumpScale
            0.060f, // cliffChance
            0.025f, // cliffStrength
            22f, // cliffScale

            0.00f,
            0.18f,
            0.00f,

            0.00f,

            C(75, 80, 75),
            C(105, 100, 90),
            C(45, 80, 95),
            C(65, 70, 68),
            C(155, 150, 130)
        ),

        B(
            "Golden Savannah",
            0.78f, 0.12f,
            0.34f, 0.14f,
            0.34f, 0.18f,

            -0.03f,
            0.72f,
            1.30f,
            0.25f,
            0.55f,
            0.45f,

            // Micro terrain: hills, bumps, cliffs
            0.040f, // hillStrength
            44f, // hillScale
            0.014f, // bumpStrength
            16f, // bumpScale
            0.050f, // cliffChance
            0.025f, // cliffStrength
            24f, // cliffScale

            0.00f,
            0.02f,
            0.04f,

            0.65f,

            C(185, 165, 70),
            C(155, 120, 65),
            C(55, 135, 200),
            C(130, 120, 80),
            C(225, 200, 120)
        ),

        B(
            "Twilight Moor",
            0.36f, 0.14f,
            0.72f, 0.14f,
            0.30f, 0.16f,

            -0.05f,
            0.72f,
            1.40f,
            0.25f,
            0.65f,
            0.65f,

            // Micro terrain: hills, bumps, cliffs
            0.035f, // hillStrength
            44f, // hillScale
            0.016f, // bumpStrength
            14f, // bumpScale
            0.060f, // cliffChance
            0.030f, // cliffStrength
            22f, // cliffScale

            0.00f,
            0.04f,
            0.00f,

            0.12f,

            C(75, 70, 105),
            C(95, 80, 100),
            C(55, 90, 150),
            C(70, 65, 95),
            C(155, 145, 165)
        )
    };
}