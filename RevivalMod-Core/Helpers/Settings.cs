﻿using BepInEx.Configuration;
using UnityEngine;

namespace RevivalMod.Helpers
{
    internal class Settings
    {
        #region Settings Properties

        // Key Bindings
        public static ConfigEntry<KeyCode> SELF_REVIVAL_KEY;
        public static ConfigEntry<KeyCode> TEAM_REVIVAL_KEY;
        public static ConfigEntry<KeyCode> GIVE_UP_KEY;

        // Revival Mechanics
        public static ConfigEntry<bool> SELF_REVIVAL_ENABLED;
        public static ConfigEntry<float> REVIVAL_HOLD_DURATION;
        public static ConfigEntry<float> TEAM_REVIVAL_HOLD_DURATION;
        public static ConfigEntry<float> REVIVAL_DURATION;
        public static ConfigEntry<float> REVIVAL_COOLDOWN;
        public static ConfigEntry<float> TIME_TO_REVIVE;
        public static ConfigEntry<bool> RESTORE_DESTROYED_BODY_PARTS;

        // Hardcore Mode
        public static ConfigEntry<bool> HARDCORE_MODE;
        public static ConfigEntry<bool> HARDCORE_HEADSHOT_DEFAULT_DEAD;
        public static ConfigEntry<float> HARDCORE_CHANCE_OF_CRITICAL_STATE;

        // Development
        public static ConfigEntry<bool> TESTING;

        #endregion

        public static void Init(ConfigFile config)
        {
            #region Key Bindings Settings

            SELF_REVIVAL_KEY = config.Bind(
                "1. Key Bindings",
                "Self Revival Key",
                KeyCode.F5,
                "The key to press and hold to revive yourself when in critical state"
            );

            TEAM_REVIVAL_KEY = config.Bind(
                "1. Key Bindings",
                "Team Revival Key",
                KeyCode.F6,
                "The key to press and hold to revive a teammate in critical state"
            );

            GIVE_UP_KEY = config.Bind(
                "1. Key Bindings",
                "Give Up Key",
                KeyCode.Backspace,
                "Press this key when in critical state to die immediately"
            );

            #endregion

            #region Revival Mechanics Settings

            SELF_REVIVAL_ENABLED = config.Bind(
                "2. Revival Mechanics",
                "Enable Self Revival",
                true,
                "When enabled, you can revive yourself with a defibrillator"
            );

            REVIVAL_HOLD_DURATION = config.Bind(
                "2. Revival Mechanics",
                "Self Revival Hold Duration",
                3f,
                "How many seconds you need to hold the Self Revival Key to revive yourself"
            );

            TEAM_REVIVAL_HOLD_DURATION = config.Bind(
                "2. Revival Mechanics",
                "Team Revival Hold Duration",
                5f,
                "How many seconds you need to hold the Team Revival Key to revive a teammate"
            );

            TIME_TO_REVIVE = config.Bind(
                "2. Revival Mechanics",
                "Critical State Duration",
                180f,
                "How long you remain in critical state before dying (in seconds)"
            );

            REVIVAL_DURATION = config.Bind(
                "2. Revival Mechanics",
                "Invulnerability Duration",
                4f,
                "How long you remain invulnerable after being revived (in seconds)"
            );

            REVIVAL_COOLDOWN = config.Bind(
                "2. Revival Mechanics",
                "Revival Cooldown",
                180f,
                "How long you must wait between revivals (in seconds)"
            );

            RESTORE_DESTROYED_BODY_PARTS = config.Bind(
                "2. Revival Mechanics",
                "Restore Destroyed Body Parts",
                false,
                "When enabled, destroyed body parts will be restored after revival (ignored in Hardcore Mode)"
            );

            #endregion

            #region Hardcore Mode Settings

            HARDCORE_MODE = config.Bind(
                "3. Hardcore Mode",
                "Enable Hardcore Mode",
                false,
                "Enables a more challenging revival experience with additional restrictions"
            );

            HARDCORE_HEADSHOT_DEFAULT_DEAD = config.Bind(
                "3. Hardcore Mode",
                "Headshots Are Fatal",
                false,
                "When enabled, headshots will kill you instantly without entering critical state"
            );

            HARDCORE_CHANCE_OF_CRITICAL_STATE = config.Bind(
                "3. Hardcore Mode",
                "Critical State Chance",
                0.75f,
                "Probability of entering critical state instead of dying instantly in Hardcore Mode (0.75 = 75%)"
            );

            #endregion

            #region Development Settings

            TESTING = config.Bind(
                "4. Development",
                "Test Mode",
                false,
                new ConfigDescription("Enables revival without requiring defibrillator item (for testing only)", null, new ConfigurationManagerAttributes { IsAdvanced = true })
            );

            #endregion
        }
    }
}