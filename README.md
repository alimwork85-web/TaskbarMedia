<p align="center"><img src="assets/banner.svg" alt="TaskbarMedia: floating media island for Windows 11" width="100%"></p>

<p align="center">
  <a href="https://github.com/alimwork85-web/TaskbarMedia/releases/latest"><img src="https://img.shields.io/github/v/release/alimwork85-web/TaskbarMedia?color=B4235F&label=release" alt="Latest release"></a>
  <a href="https://github.com/alimwork85-web/TaskbarMedia/releases"><img src="https://img.shields.io/github/downloads/alimwork85-web/TaskbarMedia/total?color=B4235F" alt="Downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/alimwork85-web/TaskbarMedia?color=555" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows11&logoColor=white" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white" alt=".NET 8">
  <img src="https://img.shields.io/badge/UI-WPF-5C2D91" alt="WPF">
  <img src="https://img.shields.io/badge/Arabic-RTL-0F7B5F" alt="Arabic RTL">
</p>

# TaskbarMedia

<p align="center"><img src="assets/screenshots/demo.gif" alt="The capsule opens into the large panel on hover, then the song changes" width="720"></p>

**Version 1.4.0** · Developer: **Ali Alhumedi** · © 2026 Ali Alhumedi

[English](#english) · [العربية](#العربية)

---

<a id="english"></a>

## English

A small tool for Windows 11: a **floating media island** that lives on its own, independent of the taskbar. A small capsule with the song's cover and playback buttons sits at the top of the screen (or wherever you choose), opens into a large panel on hover or click, and keeps its icon in the system tray. It works with any source that plays audio or video and reports itself to Windows (YouTube in the browser, YouTube Music, Spotify, media players...).

The idea comes from the "Taskbar Widget" feature in [FluentFlyout](https://github.com/unchihugo/FluentFlyout) (open source, GPL-3.0). We studied how it works and wrote our own simplified version with that feature only.

**Download:** [TaskbarMedia.exe from the latest release](https://github.com/alimwork85-web/TaskbarMedia/releases/latest) (needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)).

### Screenshots

| Disc | Ribbon |
|---|---|
| ![Large panel, Disc design](assets/screenshots/panel-disc.png) | ![Large panel, Ribbon design with an Arabic title](assets/screenshots/panel-ribbon.png) |
| ![Capsule, Disc design](assets/screenshots/capsule-disc.png) | ![Capsule, Ribbon design](assets/screenshots/capsule-ribbon.png) |

| Black colors | Glass colors |
|---|---|
| ![Large panel in Black colors](assets/screenshots/panel-black.png) | ![Large panel in Glass colors](assets/screenshots/panel-glass.png) |

A short video of the same demo: [demo.mp4](assets/screenshots/demo.mp4).

### Performance

<img src="assets/performance.svg" alt="CPU use, share of the whole processor: capsule showing 1.0%, large panel open 1.1%, Glass 1.1%" width="640">

Measured on the author's machine (16 logical cores) with a song playing: 15-second averages, two runs each.

### What it does

| Part | When it shows | What's in it |
|---|---|---|
| **Tray icon** | Always, while the tool runs | Click: show or hide the island. Right click: all settings and Exit |
| **Island (capsule)** | While something plays, per the "Show" setting | Cover + title + artist + previous / play-pause / next |
| **Large panel** | On hover over the island or a click on its cover | Large cover + title + artist + progress + shuffle / previous / play / next / repeat |

**Look and behavior:**
- **Independent of the taskbar:** the island floats above all windows, inside the work area, so it never covers the taskbar wherever it is.
- **Song colors:** the island and panel take their color from the cover, with a smooth transition when it changes. Text color (white or black) is picked to stay readable.
- **Settings on the island itself:** right-click the island or the panel for the same menu as the tray icon.
- **Nothing playing:** the island hides and only the tray icon stays.
- **Fullscreen:** the island hides while a game, presentation, or fullscreen video is in front.
- **Shuffle and repeat:** work only if the playing app supports them (Spotify yes, YouTube in the browser usually not). When unsupported they show dimmed; this is a Windows limit.
- **Repeat** cycles: off → repeat all → repeat one.
- **Moving titles:** a title or artist longer than its space slides back and forth. Arabic text displays and moves right-to-left.
- **Starts with Windows:** the EXE registers itself on first run.

### Settings

From the **right-click menu on the tray icon or the island**. Applied immediately and saved in `%AppData%\TaskbarMedia\settings.json` (a missing or damaged file restores the defaults):

| Option | Default | What it does |
|---|---|---|
| Position | Top center | Top or bottom of the screen: left, center, right |
| Show | Always while playing | "Always while playing", "Only when the song changes" (5 seconds, stays while the mouse is on it), or "Manually" from the tray icon or the shortcut |
| Open the large panel | Click on the cover | Or "Hover over the bar": opens after resting on it a moment, closes when the mouse leaves |
| Design | Disc | "Disc": the cover is a record that spins while playing, with a progress ring (in the large panel, click or drag around the ring to seek). "Ribbon": square cover with a progress line; the panel has a large title in Amiri |
| Large panel shape | Normal | "Normal" with soft corners, or "Capsule" with fully rounded ends |
| Colors | From the cover | Or "Windows color", "Black" (near-black with the cover's color as the accent), or "Glass" (the blurred cover behind the large panel) |
| Hide in fullscreen | On | Hides the island during fullscreen video or games |
| Ctrl+Alt+M shortcut | On | Hides or shows the island, like clicking the tray icon |

### Running

1. Download `TaskbarMedia.exe` from [Releases](https://github.com/alimwork85-web/TaskbarMedia/releases/latest) and run it (or build it, below, and run `dist\TaskbarMedia.exe`).
2. Needs the **.NET 8 Desktop Runtime** (already there if the .NET 8 SDK is installed).
3. On first run it adds itself to startup, so it starts with every Windows sign-in.

**Remove it from startup:** delete the `TaskbarMedia` value from:
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
```
or turn it off in Task Manager → Startup apps.

> If you move the EXE and run it from the new place, the startup path updates itself. Development builds (anything inside a `bin` folder) never register.

### Building

Needs the .NET 8 SDK.

```bash
dotnet build TaskbarMedia -c Debug
```

A single EXE in `dist`:

```bash
dotnet publish TaskbarMedia/TaskbarMedia.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o dist
```

The EXE is about 25 MB because it carries the Windows media library (`Windows.Media.Control`). The version, developer and copyright shown in the EXE's Properties come from `TaskbarMedia.csproj`.

### Project layout

| File | Role |
|---|---|
| `App.xaml(.cs)` | Entry point: single instance, load settings, start the island |
| `MediaService.cs` | Reads the current Windows media session (GSMTC) and sends commands, retrying if the connection fails at startup |
| `Island.cs` | The island: when and where it shows, opening the panel, tray icon and settings menu, shortcut, fullscreen check |
| `Settings.cs` | Saving and loading settings |
| `QuickBar.xaml(.cs)` | The island capsule |
| `NowPlayingPanel.xaml(.cs)` | The large panel |
| `ColorExtractor.cs` | Picks the main cover color and makes it usable as a background |
| `Palette.cs` | Bar and panel colors (from the cover or the theme) with the smooth transition |
| `Popups.cs` | Places the island and panel inside the work area |
| `Disc.cs` | The cover as a spinning record with the progress ring ("Disc" design) |
| `MarqueeText.cs` | A text line that slides when too long, with Arabic direction support |
| `Theme.cs` | Reads light/dark theme and the Windows accent color |
| `Autostart.cs` | Startup registration |
| `Native.cs` | Win32 declarations |
| `app.ico` | App icon |
| `tray-light.ico`, `tray-dark.ico` | Tray icon for a dark and a light taskbar |
| `Fonts/IBMPlexSansArabic-*.ttf`, `Fonts/TaskbarMediaUI.CompositeFont` | UI font: Arabic from [IBM Plex Sans Arabic](https://github.com/IBM/plex), SIL OFL 1.1 (`Fonts/IBMPlexSansArabic-OFL.txt`), Latin from Segoe UI Variable |
| `Fonts/Amiri-Regular.ttf` | Panel title font in the "Ribbon" design: [Amiri](https://github.com/aliftype/amiri), SIL OFL 1.1 (`Fonts/OFL.txt`) |
| `app.manifest` | Per-monitor DPI (PerMonitorV2) and Windows 10/11 compatibility |

### How it works

- **Media source:** `GlobalSystemMediaTransportControlsSessionManager` gives the title, artist, cover and playback state, and runs the buttons.
- **The island is its own floating window** (`WS_EX_NOACTIVATE`, above all windows), placed inside the main screen's work area (`rcWork`) so it never covers the taskbar.
- **Fullscreen** comes from `SHQueryUserNotificationState`: games (D3D) and presentation mode count directly; the "busy" state counts only when the window in front covers its whole screen, because transparent screen-sized overlays (screenshot tools and the like) also raise it.
- **Tray icon** is a WinForms `NotifyIcon` and comes back by itself after Explorer restarts.
- **A watch timer** every half second: fullscreen, Windows color, and re-placing the island after display changes.

### What's new in 1.4.0

- **Motion:** buttons respond to hover and press, the capsule fades in, the large panel grows out of it and shrinks back, titles and covers cross-fade on a new song, progress moves smoothly, and the record eases into and out of spinning. With Windows "Animation effects" off, only fades remain.
- **Look:** calmer cover-based backgrounds that keep text readable, a fine edge and soft shadow, play/pause in the record's hole ("Disc") or a filled button ("Ribbon"), dots under active shuffle/repeat, vinyl grooves and sheen on the large record, and the playing app's icon on the cover's corner.
- **New color modes:** "Black" and "Glass".
- **Font:** IBM Plex Sans Arabic for Arabic and Segoe UI Variable for Latin.
- **New icon:** a brighter pink app icon that stays visible on a dark taskbar, and a tray icon that follows the taskbar's light or dark theme.
- **Fixes:** the tooltip sits under the capsule instead of over it; right-clicking the open panel no longer closes it.

### What's new in 1.1.0

- Disc panel: clicking or dragging anywhere around the ring now seeks (before, only the thin ring line responded, so most clicks did nothing).
- Ribbon panel: a long title now scrolls instead of stretching the panel past the screen edge.
- Fullscreen: transparent screen-sized overlays from other apps no longer hide the island over normal windows.
- Developer and copyright (Ali Alhumedi) in the EXE's details.

### Notes

- **Screen-control and capture tools:** some of them minimize windows they aren't allowed to touch. The island restores itself in that case.
- **Display scaling changes:** the tool re-measures when the scale changes; only 150% was tested by hand.
- **Smooth expand:** the large panel grows from the capsule's size to full size (320 ms) and shrinks back on close (220 ms), with the capsule fading out and back.

---

<a id="العربية"></a>

<div dir="rtl">

## العربية

أداة صغيرة لـ Windows 11: **جزيرة وسائط عائمة** مستقلة عن شريط المهام. كبسولة صغيرة بغلاف الأغنية وأزرار التشغيل في أعلى الشاشة (أو الموضع الذي تختاره)، تنفتح إلى لوحة كبيرة بالتمرير أو الضغط، وتعيش أيقونتها في الـ system tray. تعمل مع أي مصدر يشغّل صوتًا أو فيديو ويعرّف نفسه لـ Windows (YouTube في المتصفح، YouTube Music، Spotify، مشغل الوسائط...).

الفكرة مستوحاة من ميزة "Taskbar Widget" في [FluentFlyout](https://github.com/unchihugo/FluentFlyout) (مفتوح المصدر، GPL-3.0). درسنا طريقة عملها وكتبنا نسخة مبسطة خاصة بنا تحتوي هذه الميزة فقط.

**التنزيل:** [TaskbarMedia.exe من آخر إصدار](https://github.com/alimwork85-web/TaskbarMedia/releases/latest) (يحتاج [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)).

### صور

| أسطوانة | شريط |
|---|---|
| ![اللوحة الكبيرة بتصميم أسطوانة](assets/screenshots/panel-disc.png) | ![اللوحة الكبيرة بتصميم شريط وعنوان عربي](assets/screenshots/panel-ribbon.png) |
| ![الكبسولة بتصميم أسطوانة](assets/screenshots/capsule-disc.png) | ![الكبسولة بتصميم شريط](assets/screenshots/capsule-ribbon.png) |

| ألوان "أسود" | ألوان "زجاج" |
|---|---|
| ![اللوحة الكبيرة بألوان أسود](assets/screenshots/panel-black.png) | ![اللوحة الكبيرة بألوان زجاج](assets/screenshots/panel-glass.png) |

مقطع فيديو قصير: [demo.mp4](assets/screenshots/demo.mp4)، والصورة المتحركة في أعلى الصفحة.

### الأداء

استهلاك المعالج من المعالج كله والأغنية تعمل (متوسط 15 ثانية، مرتين لكل حالة، على جهاز بـ 16 نواة منطقية): الكبسولة ظاهرة 1.0%، اللوحة الكبيرة مفتوحة 1.1%، ووضع "زجاج" 1.1%. الرسم البياني في القسم الإنجليزي.

### ما الذي تفعله

| الجزء | متى يظهر | ماذا فيه |
|---|---|---|
| **أيقونة الـ tray** | دائمًا ما دامت الأداة تعمل | ضغطة: إظهار الجزيرة أو إخفاؤها. الزر الأيمن: كل الإعدادات والخروج |
| **الجزيرة (الكبسولة)** | حسب خيار "الظهور" ما دام هناك مصدر تشغيل | غلاف + العنوان + الفنان + السابق / تشغيل-إيقاف / التالي |
| **اللوحة الكبيرة** | بالتمرير فوق الجزيرة أو بالضغط على غلافها | غلاف كبير + العنوان + الفنان + شريط التقدم + خلط / سابق / تشغيل / تالي / تكرار |

**تفاصيل الشكل والسلوك:**
- **مستقلة عن شريط المهام:** الجزيرة نافذة عائمة فوق كل النوافذ، داخل مساحة العمل فلا تغطي شريط المهام أينما كان.
- **لون الأغنية:** الجزيرة واللوحة تأخذان لونهما من غلاف الأغنية، بانتقال ناعم عند تغيّرها. لون النص (أبيض أو أسود) يُختار تلقائيًا ليبقى مقروءًا.
- **الإعدادات على الجزيرة نفسها:** الزر الأيمن على الجزيرة أو اللوحة يفتح نفس قائمة أيقونة الـ tray.
- **عند عدم التشغيل:** تختفي الجزيرة وتبقى أيقونة الـ tray وحدها.
- **ملء الشاشة:** تختفي الجزيرة أثناء لعبة أو عرض تقديمي أو فيديو بملء الشاشة في المقدمة.
- **الخلط والتكرار:** يعملان فقط إذا كان التطبيق المشغِّل يدعمهما (Spotify نعم، YouTube في المتصفح غالبًا لا). عند عدم الدعم يظهران باهتين، وهذا حد من Windows نفسه.
- **التكرار** يدور بين: بدون تكرار ← تكرار الكل ← تكرار الأغنية.
- **العناوين المتحركة:** العنوان أو اسم الفنان إذا كان أطول من المساحة ينزلق ذهابًا وإيابًا. النص العربي يُعرض ويتحرك من اليمين لليسار.
- **التشغيل مع Windows:** تسجّل الأداة نفسها تلقائيًا عند أول تشغيل للـ EXE.

### الإعدادات

من **الزر الأيمن على أيقونة الـ tray أو على الجزيرة**، وتُطبَّق فورًا وتُحفظ في `%AppData%\TaskbarMedia\settings.json` (الملف المفقود أو التالف يعيد القيم الافتراضية):

| الخيار | الافتراضي | ماذا يفعل |
|---|---|---|
| الموضع | أعلى الوسط | أعلى أو أسفل الشاشة: يسار، وسط، يمين |
| الظهور | دائمًا أثناء التشغيل | "دائمًا أثناء التشغيل"، أو "عند تغيير الأغنية فقط" (تظهر 5 ثوانٍ وتبقى ما دام الماوس عليها)، أو "يدويًا" من أيقونة الـ tray أو الاختصار |
| فتح اللوحة الكبيرة | بالضغط على الغلاف | أو "بالتمرير فوق الشريط": تنفتح بالوقوف عليه لحظة وتنغلق بابتعاد الماوس عنها |
| التصميم | أسطوانة | "أسطوانة": الغلاف قرص يدور أثناء التشغيل وحوله حلقة التقدم (في اللوحة الكبيرة انقر أو اسحب حول الحلقة للتقديم). "شريط": غلاف مربع وخط تقدم، واللوحة بعنوان كبير بخط Amiri |
| شكل اللوحة الكبيرة | عادي | "عادي" بزوايا خفيفة أو "كبسولة" بأطراف دائرية كاملة |
| الألوان | من غلاف الأغنية | أو "لون ويندوز"، أو "أسود" (خلفية سوداء ولون الغلاف للحلقة والتقدم)، أو "زجاج" (الغلاف مموّهًا خلف اللوحة الكبيرة) |
| إخفاء في وضع ملء الشاشة | مفعّل | تختفي الجزيرة أثناء الفيديو أو اللعبة بملء الشاشة |
| اختصار Ctrl+Alt+M | مفعّل | يخفي الجزيرة أو يظهرها، مثل الضغط على أيقونة الـ tray |

### التشغيل

1. نزّل `TaskbarMedia.exe` من [Releases](https://github.com/alimwork85-web/TaskbarMedia/releases/latest) وشغّله (أو ابنه كما في الأسفل وشغّل `dist\TaskbarMedia.exe`).
2. يحتاج **.NET 8 Desktop Runtime** (موجود مسبقًا إذا كان .NET 8 SDK مثبتًا).
3. عند أول تشغيل تُضاف الأداة إلى بدء التشغيل، فتعمل تلقائيًا مع كل دخول إلى Windows.

**إزالتها من بدء التشغيل:** احذف القيمة `TaskbarMedia` من المفتاح:
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
```
أو أوقفها من "إدارة المهام" ← "تطبيقات بدء التشغيل".

> إذا نقلت الـ EXE إلى مكان آخر وشغّلته من هناك، يُحدَّث مسار بدء التشغيل تلقائيًا. نسخ التطوير (أي شيء داخل مجلد `bin`) لا تسجّل نفسها أبدًا.

### البناء

يحتاج .NET 8 SDK.

```bash
dotnet build TaskbarMedia -c Debug
```

بناء EXE واحد في مجلد `dist`:

```bash
dotnet publish TaskbarMedia/TaskbarMedia.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o dist
```

حجم الـ EXE حوالي 25 MB لأنه يضم مكتبة Windows للوسائط (`Windows.Media.Control`). رقم الإصدار واسم المطوّر وحقوق النشر التي تظهر في خصائص الـ EXE تأتي من `TaskbarMedia.csproj`.

### بنية المشروع

| الملف | دوره |
|---|---|
| `App.xaml(.cs)` | نقطة البداية: نسخة واحدة فقط، تحميل الإعدادات، وتشغيل الجزيرة |
| `MediaService.cs` | قراءة جلسة الوسائط الحالية من Windows (GSMTC) وإرسال الأوامر، مع إعادة المحاولة إن فشل الاتصال عند البدء |
| `Island.cs` | الجزيرة: متى تظهر وأين، فتح اللوحة، أيقونة الـ tray وقائمة الإعدادات، الاختصار، وفحص ملء الشاشة |
| `Settings.cs` | حفظ الإعدادات وقراءتها |
| `QuickBar.xaml(.cs)` | كبسولة الجزيرة |
| `NowPlayingPanel.xaml(.cs)` | اللوحة الكبيرة |
| `ColorExtractor.cs` | استخراج اللون الغالب من الغلاف وجعله صالحًا كخلفية |
| `Palette.cs` | ألوان الشريط واللوحة (من الغلاف أو من الثيم) مع الانتقال الناعم |
| `Popups.cs` | وضع الجزيرة واللوحة داخل مساحة العمل |
| `Disc.cs` | الغلاف كأسطوانة تدور مع حلقة التقدم (تصميم "أسطوانة") |
| `MarqueeText.cs` | سطر نص ينزلق عند طوله، مع دعم اتجاه العربية |
| `Theme.cs` | قراءة الثيم الفاتح/الداكن ولون Windows المميز |
| `Autostart.cs` | التسجيل في بدء التشغيل |
| `Native.cs` | تعريفات Win32 المستخدمة |
| `app.ico` | أيقونة التطبيق |
| `tray-light.ico`، `tray-dark.ico` | أيقونة الـ tray لشريط مهام داكن وفاتح |
| `Fonts/IBMPlexSansArabic-*.ttf`، `Fonts/TaskbarMediaUI.CompositeFont` | خط الواجهة: العربية من [IBM Plex Sans Arabic](https://github.com/IBM/plex)، رخصة SIL OFL 1.1 (`Fonts/IBMPlexSansArabic-OFL.txt`)، واللاتينية من Segoe UI Variable |
| `Fonts/Amiri-Regular.ttf` | خط عنوان اللوحة في تصميم "شريط": [Amiri](https://github.com/aliftype/amiri)، رخصة SIL OFL 1.1 (`Fonts/OFL.txt`) |
| `app.manifest` | دعم DPI لكل شاشة (PerMonitorV2) وتوافق Windows 10/11 |

### كيف تعمل (تقنيًا)

- **مصدر الوسائط:** `GlobalSystemMediaTransportControlsSessionManager` يعطي العنوان والفنان والغلاف وحالة التشغيل، وعبره تعمل الأزرار.
- **الجزيرة نافذة عائمة مستقلة** (`WS_EX_NOACTIVATE`، فوق كل النوافذ) تُوضع داخل مساحة العمل (`rcWork`) للشاشة الرئيسية، فلا تغطي شريط المهام.
- **ملء الشاشة** يُعرف من `SHQueryUserNotificationState`: الألعاب (D3D) ووضع العرض التقديمي يُحسبان مباشرة، أما حالة "مشغول" فلا تُحسب إلا إذا كانت النافذة الأمامية تغطي شاشتها كاملة، لأن الطبقات الشفافة بحجم الشاشة (أدوات التقاط الشاشة ونحوها) ترفعها أيضًا.
- **أيقونة الـ tray** من `NotifyIcon` (WinForms)، وتعود وحدها بعد إعادة تشغيل Explorer.
- **مؤقت متابعة** كل نصف ثانية: ملء الشاشة، ولون Windows، وإعادة وضع الجزيرة بعد تغيّر الشاشة.

### الجديد في 1.4.0

- **الحركة:** الأزرار تستجيب للتمرير والضغط، والكبسولة تظهر بتلاشٍ، واللوحة الكبيرة تنمو منها وتنكمش إليها، والعنوان والغلاف يتبدلان بتلاشٍ ناعم عند تغيّر الأغنية، والتقدم يزحف بنعومة، والأسطوانة تتسارع وتتباطأ. عند إيقاف "تأثيرات الحركة" في ويندوز يبقى التلاشي فقط.
- **الشكل:** خلفيات من الغلاف أهدأ تُبقي النص واضحًا، وحافة رفيعة وظل ناعم، وزر التشغيل في ثقب الأسطوانة ("أسطوانة") أو زر ممتلئ ("شريط")، ونقطة تحت العشوائي والتكرار عند تفعيلهما، وأخاديد ولمعة على الأسطوانة الكبيرة، وأيقونة التطبيق المشغِّل على زاوية الغلاف.
- **وضعا ألوان جديدان:** "أسود" و"زجاج".
- **الخط:** IBM Plex Sans Arabic للعربية وSegoe UI Variable للاتينية.
- **أيقونة جديدة:** وردية أفتح تظهر بوضوح على شريط المهام الداكن، وأيقونة tray تتبع ثيم الشريط الفاتح أو الداكن.
- **إصلاحات:** التلميح صار تحت الكبسولة لا فوقها، والضغط بالزر الأيمن على اللوحة المفتوحة لم يعد يغلقها.

### الجديد في 1.1.0

- لوحة "أسطوانة": النقر أو السحب في أي مكان حول الحلقة يقدّم الأغنية الآن (كان خط الحلقة الرفيع وحده يستجيب، فتضيع أغلب النقرات).
- لوحة "شريط": العنوان الطويل يتحرك الآن بدل أن يمدّ اللوحة خارج حافة الشاشة.
- ملء الشاشة: الطبقات الشفافة بحجم الشاشة من برامج أخرى لم تعد تخفي الجزيرة فوق النوافذ العادية.
- اسم المطوّر وحقوق النشر (Ali Alhumedi) في تفاصيل الـ EXE.

### مشاكل واجهناها وحلولها

أغلب هذه المشاكل كانت مع الأيقونة القديمة داخل شريط المهام، قبل تحويل الأداة إلى جزيرة مستقلة.

| المشكلة | السبب | الحل |
|---|---|---|
| انهيار عند البدء أحيانًا | فشل الاتصال بخدمة الوسائط في بعض البيئات | إعادة المحاولة كل 2 ثانية تتباعد حتى 30 ثانية |
| الشريط فوق الساعة في Windows العربي | شريط المهام معكوس (RTL) والساعة على اليسار | حساب الموضع حسب اتجاه شريط المهام |
| تغطية أيقونات الـ tray | `TrayNotifyWnd` أضيق من الـ tray الحقيقي في Windows 11 | قراءة الـ tray عبر UI Automation |
| تغطية أيقونات التطبيقات | المساحة الفارغة صغيرة | تحويل الشريط إلى أيقونة واحدة + شريط عند التمرير |
| نافذة الأيقونة أعرض من اللازم | Windows يفرض حدًا أدنى لعرض النوافذ (~132 نقطة) | خفض الحد الأدنى عبر `WM_GETMINMAXINFO` |
| وميض الأيقونة عند التمرير | كنافذة داخل شريط المهام المعكوس، كان WPF يضعها في الموضع المعكوس مع كل إعادة رسم | تحويلها إلى نافذة مملوكة فوق شريط المهام |
| اختفاء الأيقونة بعد إعادة تشغيل Explorer | النافذة تُدمَّر مع شريط المهام القديم ولا يُبلغ WPF بذلك | فحص دوري لوجود النافذة وإعادة إنشائها |

### ملاحظات

- **الاختبار بأدوات التحكم بالشاشة:** بعض أدوات التقاط الشاشة أو التحكم الآلي تصغّر النوافذ غير المسموح بها مؤقتًا. أضفنا استعادة تلقائية لهذه الحالة، فلا تؤثر على الاستخدام العادي.
- **تغيير مقياس الشاشة:** الأداة مبنية لإعادة القياس عند تغيّره، لكن 150% فقط اختُبر يدويًا.
- **التمدد السلس:** اللوحة الكبيرة تنمو من حجم الكبسولة إلى حجمها الكامل (320 ms) وتنكمش إليها عند الإغلاق (220 ms)، والكبسولة تتلاشى وتعود معها.

</div>

---

**License:** MIT © 2026 Ali Alhumedi · الرخصة: MIT © 2026 Ali Alhumedi

#Windows11 #MediaPlayer #NowPlaying #Taskbar #DynamicIsland #WPF #DotNet #CSharp #Spotify #YouTubeMusic #FluentDesign #OpenSource #Arabic #RTL #ويندوز #مشغل_وسائط #مفتوح_المصدر
