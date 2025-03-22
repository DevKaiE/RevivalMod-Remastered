﻿using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RevivalMod.Constants;
using EFT.InventoryLogic;
using UnityEngine;
using EFT.Communications;
using Comfort.Common;
using RevivalMod.Helpers;
using RevivalMod.Fika;
using EFT.Interactive;
using RevivalMod.Components;
using Fika.Core.Coop.Utils;
using Fika.Core.Networking;
using EFT.UI;

namespace RevivalMod.Features
{
    /// <summary>
    /// Enhanced revival feature with manual activation and temporary invulnerability with restrictions
    /// </summary>
    internal class RevivalFeatures : ModulePatch
    {
        // New constants for effects
        private static readonly float MOVEMENT_SPEED_MULTIPLIER = 0.1f; // 40% normal speed during invulnerability
        private static readonly bool FORCE_CROUCH_DURING_INVULNERABILITY = false; // Force player to crouch during invulnerability
        private static readonly bool DISABLE_SHOOTING_DURING_INVULNERABILITY = false; // Disable shooting during invulnerability
        private static float _notificationTimer = 0f;
        private const float NOTIFICATION_INTERVAL = 15f;

        // States
        private static Dictionary<string, long> _lastRevivalTimesByPlayer = new Dictionary<string, long>();
        private static Dictionary<string, bool> _playerInCriticalState = new Dictionary<string, bool>();
        private static Dictionary<string, EDamageType> _playerInCriticalStateDamageInfo = new Dictionary<string, EDamageType>();
        private static Dictionary<string, bool> _playerIsInvulnerable = new Dictionary<string, bool>();
        private static Dictionary<string, float> _playerInvulnerabilityTimers = new Dictionary<string, float>();
        private static Dictionary<string, float> _playerCriticalStateTimers = new Dictionary<string, float>();
        //private static Dictionary<string, float> _originalAwareness = new Dictionary<string, float>(); // Renamed from _criticalModeTags
        private static Dictionary<string, float> _originalMovementSpeed = new Dictionary<string, float>(); // Store original movement speed
        private static Dictionary<string, EFT.PlayerAnimator.EWeaponAnimationType> _originalWeaponAnimationType = new Dictionary<string, PlayerAnimator.EWeaponAnimationType>();
        private static Player PlayerClient { get; set; } = null;
        private static Dictionary<string, bool> _revivablePlayers = new Dictionary<string, bool>();

        public static Dictionary<string, bool> KillOverridePlayers = new Dictionary<string, bool>();

        protected override MethodBase GetTargetMethod()
        {
            // We're patching the Update method of Player to constantly check for revival key press
            return AccessTools.Method(typeof(Player), nameof(Player.UpdateTick));
        }

        [PatchPostfix]
        static void Postfix(Player __instance)
        {
            try
            {
                string playerId = __instance.ProfileId;
                PlayerClient = __instance;

                // Only proceed for the local player
                if (!__instance.IsYourPlayer)
                    return;

                // Update invulnerability timer if active
                if (_playerIsInvulnerable.TryGetValue(playerId, out bool isInvulnerable) && isInvulnerable)
                {
                    if (_playerInvulnerabilityTimers.TryGetValue(playerId, out float timer))
                    {
                        timer -= Time.deltaTime;
                        _playerInvulnerabilityTimers[playerId] = timer;

                        // Force player to crouch during invulnerability
                        if (FORCE_CROUCH_DURING_INVULNERABILITY)
                        {
                            // Force crouch state
                            if (__instance.MovementContext.PoseLevel > 0)
                            {
                                __instance.MovementContext.SetPoseLevel(0);
                            }
                        }

                        // Disable shooting during invulnerability
                        if (DISABLE_SHOOTING_DURING_INVULNERABILITY)
                        {
                            // Block shooting by canceling fire operations
                            if (__instance.HandsController.IsAiming)
                            {
                                __instance.HandsController.IsAiming = false;
                            }
                        }

                        // End invulnerability if timer is up
                        if (timer <= 0)
                        {
                            EndInvulnerability(__instance);
                        }
                    }
                }

                if (_playerInCriticalState.TryGetValue(playerId, out bool inCritical) && inCritical)
                {
                    if (_playerCriticalStateTimers.TryGetValue(playerId, out float criticalTimer))
                    {
                        criticalTimer -= Time.deltaTime;
                        _playerCriticalStateTimers[playerId] = criticalTimer;

                        // If time runs out, player dies
                        if (criticalTimer <= 0)
                        {
                            ForcePlayerDeath(__instance);
                            return;
                        }

                        if (CheckRevivalItemInRaidInventory().Value && Settings.SELF_REVIVAL_ENABLED.Value)
                        {
                            if (Input.GetKeyDown(Settings.SELF_REVIVAL_KEY.Value))
                            {
                                TryPerformManualRevival(__instance);
                            }
                        }
                        else if (criticalTimer % 30 < 0.1f && criticalTimer > 0.5f) // Show notification every 30 seconds
                        {
                            NotificationManagerClass.DisplayMessageNotification(
                                $"Critical state: {(int)criticalTimer} seconds remaining to be revived",
                                ENotificationDurationType.Default,
                                ENotificationIconType.Alert,
                                Color.yellow);
                        }
                        else if (criticalTimer <= 30 && criticalTimer % 10 < 0.1f) // More frequent in last 30 seconds
                        {
                            NotificationManagerClass.DisplayMessageNotification(
                                $"URGENT: {(int)criticalTimer} seconds remaining before death",
                                ENotificationDurationType.Default,
                                ENotificationIconType.Alert,
                                Color.red);
                        }

                        // Check for "give up" key press
                        if (Input.GetKeyDown(Settings.GIVE_UP_KEY.Value))
                        {
                            ForcePlayerDeath(__instance);
                            return;
                        }

                       
                    }
                }

                if (CheckRevivalItemInRaidInventory().Value)
                {

                    Vector3 currentPos = __instance.Position;
                    foreach (KeyValuePair<string, Vector3> critPlayer in RMSession.GetCriticalPlayers())
                    {
                        //// Option 1: Using Vector3.Distance (simple and clear)
                        //if (Vector3.Distance(currentPos, critPlayer.Value) <= 2f)
                        //{
                        //    // The critPlayer is within 2 meters of the player.
                        //}

                        //Option 2: Using squared magnitude for performance(avoids the square root calculation)
                        if ((currentPos - critPlayer.Value).sqrMagnitude <= 4f && (!_revivablePlayers.ContainsKey(critPlayer.Key) || !_revivablePlayers[critPlayer.Key]))
                        {
                            _notificationTimer -= Time.deltaTime;
                            if (_notificationTimer <= 0)
                            {
                                _notificationTimer = NOTIFICATION_INTERVAL;
                                NotificationManagerClass.DisplayMessageNotification(
                                    $"Press {Settings.TEAM_REVIVAL_KEY.Value.ToString()} to use your defibrillator to revive your teammate!",
                                    ENotificationDurationType.Long,
                                    ENotificationIconType.Friend,
                                    Color.green);
                                Plugin.LogSource.LogDebug($"Player with id {playerId} is within 2m of critplayer with id {critPlayer}");
                            }
                            if (Input.GetKeyDown(Settings.TEAM_REVIVAL_KEY.Value))
                            {
                                bool revivalSucceeded = PerfomTeamMateRevival(critPlayer.Key, __instance);                                
                            }
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in RevivalFeatureExtension patch: {ex.Message}");
            }
        }

        public static bool IsPlayerInCriticalState(string playerId)
        {
            return _playerInCriticalState.TryGetValue(playerId, out bool inCritical) && inCritical;
        }

        public static void SetPlayerCriticalState(Player player, bool criticalState, EDamageType damageType)
        {
            if (player == null)
                return;

            string playerId = player.ProfileId;           

            // Update critical state
            _playerInCriticalState[playerId] = criticalState;
            _playerInCriticalStateDamageInfo[playerId] = damageType;

            if (criticalState)
            {
           
                // Set the critical state timer
                _playerCriticalStateTimers[playerId] = Settings.TIME_TO_REVIVE.Value;

                _playerIsInvulnerable[playerId] = true;


                // Apply tremor effect without healing
                ApplyCriticalEffects(player);


                // Make player invisible to AI - fixed implementation
                ApplyRevivableStatePlayer(player);

                if (player.IsYourPlayer)
                {
                    try
                    {
                        // Show revival options message
                        string message = "CRITICAL CONDITION!\n";

                        if (Settings.SELF_REVIVAL_ENABLED.Value && CheckRevivalItemInRaidInventory().Value)
                        {
                            message += $"Press {Settings.SELF_REVIVAL_KEY.Value.ToString()} to use defibrillator\n";
                        }

                        message += $"Press {Settings.GIVE_UP_KEY.Value.ToString()} to give up\n";
                        message += $"Or wait for a teammate to revive you ({(int)Settings.TIME_TO_REVIVE.Value} seconds)";

                        NotificationManagerClass.DisplayMessageNotification(
                            message,
                            ENotificationDurationType.Long,
                            ENotificationIconType.Default,
                            Color.red);
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogError($"Error displaying critical state UI: {ex.Message}");
                    }
                }
            }
            else
            {
                // Remove from critical state timer tracking
                _playerCriticalStateTimers.Remove(playerId);

                // If player is leaving critical state without revival (e.g., revival failed),
                // make sure to remove stealth from player and disable invulnerability
                if (!_playerInvulnerabilityTimers.ContainsKey(playerId))
                {
                    RemoveStealthFromPlayer(player);
                    _playerIsInvulnerable.Remove(playerId);

                    // Remove any applied effects
                    RestorePlayerMovement(player);
                }
            }
        }

        // Apply effects for critical state without healing
        private static void ApplyCriticalEffects(Player player)
        {
            try
            {
                string playerId = player.ProfileId;

                // Store original movement speed
                if (!_originalMovementSpeed.ContainsKey(playerId))
                {
                    _originalMovementSpeed[playerId] = player.Physical.WalkSpeedLimit;
                }

                // Apply tremor effect
                player.ActiveHealthController.DoContusion(Settings.REVIVAL_DURATION.Value, 1f);
                player.ActiveHealthController.DoStun(Settings.REVIVAL_DURATION.Value / 2, 1f);

                // Severe movement restrictions - extremely slow movement
                player.Physical.WalkSpeedLimit = 0f; // No movement

                // Restrict player to crouch-only
                if (player.MovementContext != null)
                {
                    // Force crouch
                    player.MovementContext.SetPoseLevel(0);
                    player.ResetLookDirection();

                    // Disable sprinting
                    player.ActiveHealthController.AddFatigue();
                    player.ActiveHealthController.SetStaminaCoeff(0f);
                    //// Force movement state to be limited
                    //typeof(EFT.Player.MovementContext).GetMethod("AddStateSpeedLimit",
                    //    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    //    ?.Invoke(player.MovementContext, new object[] { "critical_state", 0.05f });
                }

                Plugin.LogSource.LogDebug($"Applied critical effects to player {playerId}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error applying critical effects: {ex.Message}");
            }
        }

        // Restore player movement after invulnerability ends
        private static void RestorePlayerMovement(Player player)
        {
            try
            {
                string playerId = player.ProfileId;

                // Restore original movement speed if we stored it
                if (_originalMovementSpeed.TryGetValue(playerId, out float originalSpeed))
                {
                    player.Physical.WalkSpeedLimit = originalSpeed;
                    _originalMovementSpeed.Remove(playerId);
                }

                Plugin.LogSource.LogDebug($"Restored movement for player {playerId}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error restoring player movement: {ex.Message}");
            }
        }

        // Method to make player invisible to AI - improved implementation
        private static void  ApplyRevivableStatePlayer(Player player)
        {
            try
            {
                string playerId = player.ProfileId;

                //// Skip if already applied
                //if (_originalAwareness.ContainsKey(playerId))
                //    return;

                //// Store original awareness value
                //_originalAwareness[playerId] = player.Awareness;

                //// Set awareness to 0 to make bots not detect the player
                //player.Awareness = 0f;
                player.PlayDeathSound();
                player.HandsController.IsAiming = false;
                player.MovementContext.EnableSprint(false);
                player.MovementContext.SetPoseLevel(0f, true);
                player.MovementContext.IsInPronePose = true;
                player.SetEmptyHands(null);
                player.ActiveHealthController.IsAlive = false;
                FikaInterface.SendPlayerPositionPacket(playerId, new DateTime(), player.Position);
                Plugin.LogSource.LogDebug($"Applied improved stealth mode to player {playerId}");
                Plugin.LogSource.LogDebug($"Stealth Mode Variables, Current Awareness: {player.Awareness}, IsAlive: {player.ActiveHealthController.IsAlive}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error applying stealth mode: {ex.Message}");
            }
        }

        // Method to remove invisibility from player
        private static void RemoveStealthFromPlayer(Player player)
        {
            try
            {
                string playerId = player.ProfileId;
                //if (!_originalAwareness.ContainsKey(playerId)) return;

                //player.Awareness = _originalAwareness[playerId];
                //_originalAwareness.Remove(playerId);

                player.IsVisible = true;
                player.ActiveHealthController.IsAlive = true;
                player.ActiveHealthController.DoContusion(25f, 0.25f);
                player.Awareness = 350f;

                Plugin.LogSource.LogInfo($"Removed stealth mode from player {playerId}");
                
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error removing stealth mode: {ex.Message}");
            }
        }

        public static KeyValuePair<string, bool> CheckRevivalItemInRaidInventory()
        {

            try
            {
                if (PlayerClient == null)
                {
                    if (Singleton<GameWorld>.Instantiated)
                    {
                        PlayerClient = Singleton<GameWorld>.Instance.MainPlayer;
                    }
                    else
                    {
                        return new KeyValuePair<string, bool>(string.Empty, false);
                    }
                }

                if (PlayerClient == null)
                {
                    return new KeyValuePair<string, bool>(string.Empty, false);
                }

                string playerId = PlayerClient.ProfileId;
                var inRaidItems = PlayerClient.Inventory.GetPlayerItems(EPlayerItems.Equipment);
                bool hasItem = inRaidItems.Any(item => item.TemplateId == Constants.Constants.ITEM_ID);

                return new KeyValuePair<string, bool>(playerId, hasItem);
            }
            catch (Exception ex)
            {
                return new KeyValuePair<string, bool>(string.Empty, false);
            }
        }


        public static bool TryPerformManualRevival(Player player)
        {
            if (player == null)
                return false;

            string playerId = player.ProfileId;

            // Check if the player has the revival item
            bool hasDefib = CheckRevivalItemInRaidInventory().Value;

            // Check if the revival is on cooldown
            bool isOnCooldown = false;
            if (_lastRevivalTimesByPlayer.TryGetValue(playerId, out long lastRevivalTime))
            {
                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                isOnCooldown = (currentTime - lastRevivalTime) < Settings.REVIVAL_COOLDOWN.Value;
            }

            if (isOnCooldown)
            {
                // Calculate remaining cooldown
                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int remainingCooldown = (int)(Settings.REVIVAL_COOLDOWN.Value - (currentTime - lastRevivalTime));

                NotificationManagerClass.DisplayMessageNotification(
                    $"Revival on cooldown! Available in {remainingCooldown} seconds",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Alert,
                    Color.yellow);
                if (!Settings.TESTING.Value) return false;

            }

            if (hasDefib || Settings.TESTING.Value)
            {
                // Consume the item
                if (hasDefib && !Settings.TESTING.Value)
                {
                    ConsumeDefibItem(player);
                }

                FikaInterface.SendRemovePlayerFromCriticalPlayersListPacket(playerId);

                // Apply revival effects - now with limited healing
                ApplyRevivalEffects(player);

                // Apply invulnerability
                StartInvulnerability(player);

                player.Say(EPhraseTrigger.OnMutter, false, 2f, ETagStatus.Combat, 100, true);

                // Reset critical state
                _playerInCriticalState[playerId] = false;



                // Set last revival time
                _lastRevivalTimesByPlayer[playerId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // Show successful revival notification
                NotificationManagerClass.DisplayMessageNotification(
                    "Defibrillator used successfully! You are temporarily invulnerable but limited in movement.",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Default,
                    Color.green);

                Plugin.LogSource.LogInfo($"Manual revival performed for player {playerId}");
                return true;
            }
            else
            {
                NotificationManagerClass.DisplayMessageNotification(
                    "No defibrillator found! Unable to revive!",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Alert,
                    Color.red);

                return false;
            }
        }

        public static bool PerfomTeamMateRevival(string playerId, Player reviver)
        {
            try
            {
                NotificationManagerClass.DisplayMessageNotification(
                   "Attempting to revive teammate...",
                   ENotificationDurationType.Default,
                   ENotificationIconType.Friend,
                   Color.green);

                ConsumeDefibItem(reviver);
                RMSession.RemovePlayerFromCriticalPlayers(playerId);
                    
                FikaInterface.SendRemovePlayerFromCriticalPlayersListPacket(playerId);
                FikaInterface.SendReviveMePacket(playerId, reviver.ProfileId);
                      
                return true;
    
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError(e);
                return false;
            }
        }

        public static bool TryPerformRevivalByTeamMate(string playerId)
        {
           
            if (playerId != Singleton<GameWorld>.Instance.MainPlayer.ProfileId) return false;
            Player player = Singleton<GameWorld>.Instance.MainPlayer;

            try {
            // Apply revival effects - now with limited healing
            ApplyRevivalEffects(player);

            // Apply invulnerability
            StartInvulnerability(player);

            // Reset critical state
            _playerInCriticalState[playerId] = false;



            // Set last revival time
            _lastRevivalTimesByPlayer[playerId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Show successful revival notification
            NotificationManagerClass.DisplayMessageNotification(
                "Defibrillator used successfully! You are temporarily invulnerable but limited in movement.",
                ENotificationDurationType.Long,
                ENotificationIconType.Default,
                Color.green);

            Plugin.LogSource.LogInfo($"Manual revival performed for player {playerId}");
            return true;
            }
            catch(Exception e) {
                Plugin.LogSource.LogError(e);
               return false;
            }
        }

        // Fix the type parameter issue by ensuring MedKitComponent implements IItemComponent
        private static void ConsumeDefibItem(Player player)
        {
            try
            {
                var inRaidItems = player.Inventory.GetPlayerItems(EPlayerItems.Equipment);
                Item defibItem = inRaidItems.FirstOrDefault(item => item.TemplateId == Constants.Constants.ITEM_ID);
                Plugin.LogSource.LogDebug($"Found defib item: {defibItem?.TemplateId}");
                defibItem.GetItemComponent<MedKitComponent>().HpResource = 0f;
                ItemAddress itemAdress = defibItem.GetItemComponent<MedKitComponent>().Item.CurrentAddress;
                itemAdress.RemoveWithoutRestrictions(defibItem);

                if (defibItem != null)
                {

                    ItemUiContext context = ItemUiContext.Instance;
                    context.UseAll(defibItem);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error consuming defib item: {ex.Message}");
            }
        }

        private static void ApplyRevivalEffects(Player player)
        {
            try
            {
                // Modified to provide limited healing instead of full healing
                ActiveHealthController healthController = player.ActiveHealthController;
                if (healthController == null)
                {
                    Plugin.LogSource.LogError("Could not get ActiveHealthController");
                    return;
                }

                // Remove negative effects
                //RemoveAllNegativeEffects(healthController);

                if (!Settings.HARDCORE_MODE.Value && Settings.RESTORE_DESTROYED_BODY_PARTS.Value) {
                    // Apply limited healing - enough to survive but not full health

                    foreach (EBodyPart bodyPart in Enum.GetValues(typeof(EBodyPart)))
                    {
                        Plugin.LogSource.LogDebug($"{bodyPart.ToString()} is on {healthController.GetBodyPartHealth(bodyPart).Current} health.");
                        if (healthController.GetBodyPartHealth(bodyPart).Current < 1) { 
                            healthController.FullRestoreBodyPart(bodyPart);
                            Plugin.LogSource.LogDebug($"Restored {bodyPart.ToString()}.");
                        }
                    }
                }

                //// Apply painkillers effect
                //healthController.DoPainKiller();

                // Apply tremor effect
                healthController.DoContusion(Settings.REVIVAL_DURATION.Value, 1f);
                healthController.DoStun(Settings.REVIVAL_DURATION.Value / 2, 1f);

                Plugin.LogSource.LogInfo("Applied limited revival effects to player");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error applying revival effects: {ex.Message}");
            }
        }

        private static void RemoveAllNegativeEffects(ActiveHealthController healthController)
        {
            try
            {
                MethodInfo removeNegativeEffectsMethod = AccessTools.Method(typeof(ActiveHealthController), "RemoveNegativeEffects");
                if (removeNegativeEffectsMethod != null)
                {
                    foreach (EBodyPart bodyPart in Enum.GetValues(typeof(EBodyPart)))
                    {
                        try
                        {
                            removeNegativeEffectsMethod.Invoke(healthController, new object[] { bodyPart });
                        }
                        catch { }
                    }
                    Plugin.LogSource.LogInfo("Removed all negative effects from player");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error removing effects: {ex.Message}");
            }
        }

        private static void StartInvulnerability(Player player)
        {
            if (player == null)
                return;

            string playerId = player.ProfileId;
            _playerIsInvulnerable[playerId] = true;
            _playerInvulnerabilityTimers[playerId] = Settings.REVIVAL_DURATION.Value;

            // Apply movement restrictions
            ApplyCriticalEffects(player);

            // Start coroutine for visual flashing effect
            player.StartCoroutine(FlashInvulnerabilityEffect(player));

            Plugin.LogSource.LogInfo($"Started invulnerability for player {playerId} for {Settings.REVIVAL_DURATION.Value} seconds");
        }

        private static void EndInvulnerability(Player player)
        {
            if (player == null)
                return;

            string playerId = player.ProfileId;
            _playerIsInvulnerable[playerId] = false;
            _playerInvulnerabilityTimers.Remove(playerId);

            // Remove stealth from player
            RemoveStealthFromPlayer(player);

            // Remove movement restrictions
            RestorePlayerMovement(player);

            // Show notification that invulnerability has ended
            if (player.IsYourPlayer)
            {
                NotificationManagerClass.DisplayMessageNotification(
                    "Temporary invulnerability has ended.",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Default,
                    Color.white);
            }

            Plugin.LogSource.LogInfo($"Ended invulnerability for player {playerId}");
        }

        private static IEnumerator FlashInvulnerabilityEffect(Player player)
        {
            string playerId = player.ProfileId;
            float flashInterval = 0.5f; // Flash every half second
            bool isVisible = true; // Track visibility state

            // Store original visibility states of all renderers
            Dictionary<Renderer, bool> originalStates = new Dictionary<Renderer, bool>();

            // First ensure player is visible to start
            if (player.PlayerBody != null && player.PlayerBody.BodySkins != null)
            {
                foreach (var kvp in player.PlayerBody.BodySkins)
                {
                    if (kvp.Value != null)
                    {
                        var renderers = kvp.Value.GetComponentsInChildren<Renderer>(true);
                        foreach (var renderer in renderers)
                        {
                            if (renderer != null)
                            {
                                originalStates[renderer] = renderer.enabled;
                                renderer.enabled = true;
                            }
                        }
                    }
                }
            }

            // Now flash the player model
            while (_playerIsInvulnerable.TryGetValue(playerId, out bool isInvulnerable) && isInvulnerable)
            {
                try
                {
                    isVisible = !isVisible; // Toggle visibility

                    // Apply visibility to all renderers in the player model
                    if (player.PlayerBody != null && player.PlayerBody.BodySkins != null)
                    {
                        foreach (var kvp in player.PlayerBody.BodySkins)
                        {
                            if (kvp.Value != null)
                            {
                                var renderers = kvp.Value.GetComponentsInChildren<Renderer>(true);
                                foreach (var renderer in renderers)
                                {
                                    if (renderer != null)
                                    {
                                        renderer.enabled = isVisible;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"Error in flash effect: {ex.Message}");
                }

                yield return new WaitForSeconds(flashInterval);
            }

            // Always ensure player is visible when effect ends by restoring original states
            try
            {
                foreach (var kvp in originalStates)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.enabled = true; // Force visibility on exit
                    }
                }
            }
            catch
            {
                // Last resort fallback if the dictionary approach fails
                if (player.PlayerBody != null && player.PlayerBody.BodySkins != null)
                {
                    foreach (var kvp in player.PlayerBody.BodySkins)
                    {
                        if (kvp.Value != null)
                        {
                            kvp.Value.EnableRenderers(true);
                        }
                    }
                }
            }
        }

        public static bool IsPlayerInvulnerable(string playerId)
        {
            return _playerIsInvulnerable.TryGetValue(playerId, out bool invulnerable) && invulnerable;
        }

        private static void ForcePlayerDeath(Player player)
        {
            try
            {
                string playerId = player.ProfileId;

                // Add to override list first (before any other operations)
                KillOverridePlayers[playerId] = true;

                // Clean up all state tracking for this player
                _playerIsInvulnerable[playerId] = false;
                _playerInvulnerabilityTimers.Remove(playerId);
                _playerInCriticalState[playerId] = false;
                _playerCriticalStateTimers.Remove(playerId);

                // Remove player from critical players list for network sync
                RMSession.RemovePlayerFromCriticalPlayers(playerId);
                FikaInterface.SendRemovePlayerFromCriticalPlayersListPacket(playerId);

                // Show notification about death
                NotificationManagerClass.DisplayMessageNotification(
                    "You have died.",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Alert,
                    Color.red);

                // Get the damage type before killing
                EDamageType damageType = _playerInCriticalStateDamageInfo[playerId];

                // Use reflection to directly call the original Kill method, bypassing Harmony patches
                // This is a more direct approach to avoid our own patch
                player.ActiveHealthController.IsAlive = true;
                player.ActiveHealthController.Kill(damageType);

                Plugin.LogSource.LogInfo($"Player {playerId} has died after critical state");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error forcing player death: {ex.Message}");
            }
        }
    }
}