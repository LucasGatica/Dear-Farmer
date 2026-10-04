using System;
using DearFarmer.Framework;
using DearFarmer.UI;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace DearFarmer
{
    /// <summary>Dear Farmer: cartas anônimas entre jogadores, entregues na caixa de correio.</summary>
    public sealed class ModEntry : Mod
    {
        private ModConfig Config;
        private ModServices S;

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            this.Config.Sanitize();

            var store = new StateStore(helper, this.Monitor);
            store.Load();

            this.S = new ModServices
            {
                Helper = helper,
                Monitor = this.Monitor,
                Manifest = this.ModManifest,
                Config = () => this.Config,
                Store = store,
                Api = new ApiClient(this.Monitor, () => this.Config.ApiBaseUrl, () => store.State.Token, this.ModManifest.Version.ToString()),
            };
            this.S.Delivery = new DeliveryService(this.S);

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.GameLoop.DayEnding += this.OnDayEnding;
            helper.Events.GameLoop.Saving += (_, _) => store.Save();
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => store.Save();
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Display.MenuChanged += (_, e) => this.S.Delivery.OnMenuChanged(e);
            helper.Events.Content.AssetRequested += (_, e) => this.S.Delivery.OnAssetRequested(e);

            if (!this.S.Api.IsConfigured)
                this.Monitor.Log("ApiBaseUrl não configurada em config.json: as cartas não serão enviadas nem recebidas até configurar o servidor.", LogLevel.Warn);
        }

        // ------------------------------------------------------------------
        // Eventos
        // ------------------------------------------------------------------

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            this.RegisterConfigMenu();
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            // garante o estado deste jogador e já começa a buscar cartas para o dia
            this.S.Store.CurrentPlayer();
            this.S.Store.Save();
            this.S.Delivery.EnsureQueueFilled();
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            this.S.Delivery.TryDeliver();
        }

        private void OnDayEnding(object sender, DayEndingEventArgs e)
        {
            this.S.Delivery.OnDayEnding();
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            this.S.DrainMainThreadQueue();
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsPlayerFree) return;
            if (Game1.keyboardDispatcher?.Subscriber != null) return; // digitando em algum lugar

            // atalho global
            if (this.Config.OpenMenuKey.JustPressed())
            {
                this.OpenMainMenu();
                return;
            }

            // clique na caixa de correio
            if (!e.Button.IsActionButton()) return;
            if (Game1.player.mailbox.Count > 0) return; // tem correio: o jogo abre normalmente (inclusive as nossas cartas)

            Vector2 tile = e.Cursor.GrabTile;
            if (!this.IsMailboxTile(Game1.currentLocation, tile)) return;
            if (!Utility.tileWithinRadiusOfPlayer((int)tile.X, (int)tile.Y, 1, Game1.player)) return;

            this.Helper.Input.Suppress(e.Button);
            this.OpenMainMenu();
        }

        private bool IsMailboxTile(GameLocation location, Vector2 tile)
        {
            if (location == null) return false;
            int x = (int)tile.X, y = (int)tile.Y;
            try
            {
                string action = location.doesTileHaveProperty(x, y, "Action", "Buildings");
                if (!string.IsNullOrEmpty(action) && action.StartsWith("Mailbox", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (location is Farm farm)
                {
                    Point mailbox = farm.GetMainMailboxPosition();
                    if (mailbox.X == x && mailbox.Y == y) return true;
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Erro ao checar a caixa de correio: {ex.Message}", LogLevel.Trace);
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Menus
        // ------------------------------------------------------------------

        private void OpenMainMenu()
        {
            if (Game1.activeClickableMenu != null) return;
            GameLocation location = Game1.currentLocation;
            if (location == null) return;

            PlayerState ps = this.S.Store.CurrentPlayer();
            int unread = ps.Unread.Count;
            string receivedLabel = this.S.Text("mailbox.received") + (unread > 0 ? $" ({unread})" : "");

            var responses = new[]
            {
                new Response("write", this.S.Text("mailbox.write")),
                new Response("received", receivedLabel),
                new Response("mine", this.S.Text("mailbox.mine")),
                new Response("design", this.S.Text("mailbox.design")),
                new Response("close", this.S.Text("mailbox.close")),
            };

            location.createQuestionDialogue(this.S.Text("mailbox.question"), responses, (who, answer) =>
            {
                switch (answer)
                {
                    case "write":
                        this.OpenWriteMenu();
                        break;
                    case "received":
                        Game1.activeClickableMenu = new LetterListMenu(this.S, LetterListMenu.ListMode.Received);
                        break;
                    case "mine":
                        Game1.activeClickableMenu = new LetterListMenu(this.S, LetterListMenu.ListMode.Mine);
                        break;
                    case "design":
                        Game1.activeClickableMenu = new LetterDesignMenu(this.S);
                        break;
                }
            });
        }

        private void OpenWriteMenu()
        {
            int max = this.Config.MaxSentPerGameDay;
            if (this.S.Store.RemainingSendsToday(max) <= 0)
            {
                Game1.drawObjectDialogue(this.S.Text("error.dailyLimit", new { max }));
                return;
            }
            if (!this.S.Api.IsConfigured)
            {
                Game1.drawObjectDialogue(this.S.Text("error.network"));
                return;
            }
            Game1.activeClickableMenu = new WriteLetterMenu(this.S);
        }

        // ------------------------------------------------------------------
        // Generic Mod Config Menu (opcional)
        // ------------------------------------------------------------------

        private void RegisterConfigMenu()
        {
            IGenericModConfigMenuApi gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm == null) return;

            IManifest m = this.ModManifest;
            gmcm.Register(m,
                reset: () => { this.Config = new ModConfig(); this.Config.Sanitize(); },
                save: () => { this.Config.Sanitize(); this.Helper.WriteConfig(this.Config); });

            gmcm.AddTextOption(m, () => this.Config.ApiBaseUrl, v => this.Config.ApiBaseUrl = v, () => this.S.Text("config.apiBaseUrl.name"), () => this.S.Text("config.apiBaseUrl.tooltip"));
            gmcm.AddNumberOption(m, () => this.Config.LettersPerDay, v => this.Config.LettersPerDay = v, () => this.S.Text("config.lettersPerDay.name"), () => this.S.Text("config.lettersPerDay.tooltip"), 0, 5);
            gmcm.AddNumberOption(m, () => this.Config.MaxSentPerGameDay, v => this.Config.MaxSentPerGameDay = v, () => this.S.Text("config.maxSentPerDay.name"), () => this.S.Text("config.maxSentPerDay.tooltip"), 1, 10);
            gmcm.AddBoolOption(m, () => this.Config.SignWithFarmerName, v => this.Config.SignWithFarmerName = v, () => this.S.Text("config.signWithName.name"));
            gmcm.AddKeybindList(m, () => this.Config.OpenMenuKey, v => this.Config.OpenMenuKey = v, () => this.S.Text("config.openMenuKey.name"), () => this.S.Text("config.openMenuKey.tooltip"));
            gmcm.AddBoolOption(m, () => this.Config.ShowHudMessage, v => this.Config.ShowHudMessage = v, () => this.S.Text("config.showHudMessage.name"));
            gmcm.AddBoolOption(m, () => this.Config.DeliverOnlyWhenMailboxEmpty, v => this.Config.DeliverOnlyWhenMailboxEmpty = v, () => this.S.Text("config.deliverOnlyWhenMailboxEmpty.name"), () => this.S.Text("config.deliverOnlyWhenMailboxEmpty.tooltip"));
        }
    }
}
