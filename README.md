# Ötüken

A 4v4 mobile hero brawler drawn from Turkic mythology. Two sides fight to hold the Kut Tree, the world tree at the centre of the sacred Ötüken plain. The side that holds it long enough to reach 100% wins.

| Tengri's Host | Role | Erlik's Horde |
|---|---|---|
| Alp Er Tunga | Tank | Tepegöz |
| Asena | Damage (melee) | Albastı |
| Mergen | Damage (ranged) | Şulmus |
| Umay | Support | Erlik |

You play one hero; bots fill the other seven seats. Difficulty sets how hard the rival bots hit, aim and react.

## License

**Proprietary, all rights reserved.** The code is public for viewing only. Using, copying, modifying or distributing it requires a paid written license from the author. See [LICENSE](LICENSE).

## Tech

- **Engine:** Unity 6 LTS (any 6000.x LTS release).
- **Language:** C#. Mobile builds use IL2CPP, which compiles it to native ARM64 code.
- **No art assets or scenes to assemble.** The arena, heroes, effects, sound and UI are all generated from code at start-up, in whatever scene is open.
- Works with either input setting (Input System package or the legacy Input Manager), and with either render pipeline (URP or Built-in).

## First run (Windows)

1. Install **Unity Hub**, then **Unity 6 LTS** with the **Android Build Support** module. Tick its *OpenJDK* and *Android SDK & NDK Tools* sub-items.
2. In Unity Hub choose **Add → Add project from disk** and pick this `otuken` folder. Unity creates `Packages/` and `ProjectSettings/` on first open.
3. When the editor opens, choose **Otuken → Set Up Project** from the top menu.
4. Press **Play**. Use the mouse and keyboard in the editor:
   - WASD moves
   - the mouse aims and click fires
   - E is your skill, Q your ultimate
   - Esc pauses

## Put it on an Android phone

1. On the phone, turn on Developer options and USB debugging, then connect it by USB.
2. **Otuken → Build Android APK (test on phone)**. The APK lands in `Builds/Android/`.
3. Install it with `adb install Builds/Android/Otuken.apk`. Or choose **File → Build And Run** with Android selected.

For Google Play, use **Otuken → Build Android App Bundle**. First create an upload keystore under *Project Settings → Player → Android → Publishing Settings*.

## Cloud builds (GitHub Actions, free)

`.github/workflows/build-android.yml` builds the Android APK with [GameCI](https://game.ci) on every push to `main`. You can also start it from the **Actions** tab with *Run workflow*. The finished APK is attached to the run as the **Otuken-Android** artifact. Download it, unzip it, and install it on your phone.

The build uses Unity **6000.3.29f1** (Unity 6.3 LTS). It needs three repository secrets, set once:

1. Install **Unity Hub** on your PC and sign in.
2. In Unity Hub, open **Preferences → Licenses → Add → Get a free personal license**. This writes the license file `C:\ProgramData\Unity\Unity_lic.ulf`. The ProgramData folder is hidden by default.
3. Add the secrets. GitHub CLI prompts for each value, so nothing lands in your shell history:
   ```
   gh secret set UNITY_LICENSE < "C:\ProgramData\Unity\Unity_lic.ulf"
   gh secret set UNITY_EMAIL
   gh secret set UNITY_PASSWORD
   ```
   Or use the repository's *Settings → Secrets and variables → Actions* page.
4. Sign out of Unity Hub. Signing out returns the Personal license, so the cloud build can use it.

The first build takes about 30–40 minutes. Later builds reuse the cached `Library` folder and finish faster.

## iPhone without a developer account (free)

The workflow `.github/workflows/build-ios.yml` runs manually, from **Actions → Build iOS → Run workflow**. It works in two stages:

1. A Linux runner exports the Xcode project.
2. A macOS runner compiles it into an **unsigned** `.ipa`, attached to the run as **Otuken-iOS-unsigned**.

A Mac build costs ten times the Linux rate from the free minutes, so start it only when you want a new iPhone build.

To install the `.ipa` on your iPhone from Windows:

1. Install **iTunes** from apple.com (not the Microsoft Store version) and **[Sideloadly](https://sideloadly.io)**.
2. Connect the iPhone by USB and tap *Trust this computer* on the phone.
3. In Sideloadly, drag in `Otuken-unsigned.ipa`, enter your normal Apple ID, and click **Start**. Sideloadly signs the app with a free certificate and installs it.
4. On the iPhone, open *Settings → Privacy & Security* and turn on **Developer Mode**. The phone restarts.
5. Then open *Settings → General → VPN & Device Management* and trust your Apple ID.

Limits of a free Apple ID:

- The app stops launching after **7 days**. Re-run Sideloadly to refresh it; your game data is kept.
- At most 3 sideloaded apps can be installed at once.

The paid Apple Developer Program removes these limits and adds TestFlight.

## iOS with a Mac

iOS builds need a Mac with Xcode. On the Mac, open the project and choose **Otuken → Build iOS Xcode Project**. Then open `Builds/iOS/Unity-iPhone.xcodeproj` in Xcode, set your Apple team, and run it on a device. Without a Mac, a cloud build service such as Unity Build Automation can produce the iOS build.

## Code map

| File | What it does |
|---|---|
| `Scripts/GameManager.cs` | Boots the game, runs the menu, match and pause flow, the camera, touch sticks and the HUD |
| `Scripts/Match.cs` | One match: movement, combat, abilities, spirit wolves, the capture point, scoring |
| `Scripts/Brain.cs` | Bot behaviour |
| `Scripts/HeroDb.cs` | Heroes, lore, kits and balance numbers |
| `Scripts/Arena.cs` | The map layout, obstacles and the painted ground |
| `Scripts/Unit.cs` | Hero state and the primitive-built hero models |
| `Scripts/Fx.cs`, `Sfx.cs` | Pooled effects and synthesized sounds |
| `Scripts/Controls.cs`, `Ui.cs` | Input backends and UI drawing helpers |
| `Editor/OtukenSetup.cs` | The **Otuken** menu: project setup and builds |

Balance lives in `HeroDb.cs`; capture speed and score rate are the constants at the top of `Match.cs`.
