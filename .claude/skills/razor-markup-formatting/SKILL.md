---
name: razor-markup-formatting
description: Razor markup formatting and style guide for VAM .razor files. Covers parameter formatting (one per line), spacing, indentation, self-closing tags, and code block formatting. Use when writing or formatting any .razor file.
---

# Razor Markup Formatting

## When to Use This Skill

- Writing new `.razor` files
- Formatting existing `.razor` markup
- Reviewing `.razor` file code quality
- When unsure about `.razor` formatting conventions
- Cleaning up `.razor` file structure

**This skill covers `.razor` markup formatting ONLY.** For C# code-behind (`.razor.cs`), see the **csharp-code-writer** skill.

---

## 🔴 VAM has no component library

There is no MudBlazor, no Radzen, no Fluent UI. The markup is **plain HTML elements plus VAM's own components**, styled by the CSS tokens lifted from `_MockUp/vam-console.html`, which is the design of record.

Every rule below applies identically to an HTML tag and to a VAM component. Where a component would be convenient, check `Vam.Ui/Components/` first — a second `LevelMeter` is a defect, not a convenience.

**Every `.razor` file lives in `Vam.Ui`.** `Vam.Client` and `Vam.WebClient` are hosts: a startup file and one platform-services class each. A `.razor` file outside `Vam.Ui` is a defect.

---

## 🚨 CRITICAL: Never Use @code Blocks

**NEVER use `@code { }` blocks in `.razor` files. ALWAYS create a separate `.razor.cs` code-behind file.**

```razor
@* ❌ WRONG - @code block in .razor file *@
@page "/mixer"

<h1>
    Mixer
</h1>

@code {
    List<ChannelState> channels = [];

    protected override async Task OnInitializedAsync()
    {
        channels = await Session.GetChannelsAsync();
    }
}
```

```csharp
// ✅ CORRECT - Separate .razor.cs file

// MixerView.razor
@page "/mixer"

<h1>
    Mixer
</h1>

// MixerView.razor.cs
namespace Vam.Ui.Views;

public partial class MixerView
{
    List<ChannelState> channels = [];

    protected override async Task OnInitializedAsync()
    {
        channels = await Session.GetChannelsAsync();
    }
}
```

**Why:**
- Keeps `.razor` files clean and focused on markup only
- Better separation of concerns
- Easier to read and maintain
- C# code gets proper IDE support (IntelliSense, refactoring)
- Consistent with the project's one-type-per-file rule

---

## Directive Formatting (Top of File)

Directives always go at the top of `.razor` files in this order:

```razor
@page "/mixer"
@page "/mixer/{ChannelIndex:int}"
@using Vam.Ui.Components
@using Vam.Ui.Extensions
@inject IVamSession Session
@inject NavigationManager Navigation
@implements IDisposable
```

**Rules:**
- All directives at the very top of the file
- No blank lines between directives of the same type
- Group by type: `@page`, then `@using`, then `@inject`, then `@implements` / `@attribute`
- Blank line after directives before markup starts

```razor
@* ✅ CORRECT - Proper directive ordering *@
@page "/diagnostics"
@using Vam.Ui.Components
@inject IVamSession Session
@implements IDisposable

<div class="view">
    @* Markup starts here *@
</div>

@* ❌ WRONG - Mixed order and random blank lines *@
@page "/diagnostics"

@inject IVamSession Session
@using Vam.Ui.Components

@implements IDisposable
<div class="view">
</div>
```

**Prefer `[Inject]` in the code-behind over `@inject` in markup** when the component already has a `.razor.cs`, which it always does. `@inject` is acceptable for a component small enough to have no other C#, but consistency within a folder matters more than the choice.

---

## Element Formatting

### Single-Parameter Elements

```razor
@* ✅ CORRECT - Self-closing, stays inline *@
<hr class="divider" />
<LevelMeter ChannelIndex="ChannelIndex" />

@* ✅ CORRECT - Content on new line *@
<span class="strip-name">
    @Channel.Name
</span>
<button type="button" @onclick="HandleMuteAsync">
    M
</button>
```

### Multi-Parameter Elements (2+ Parameters)

**ONE PARAMETER PER LINE — always vertically aligned**

```razor
@* ✅ CORRECT - Two parameters, each on a separate line *@
<LevelMeter ChannelIndex="ChannelIndex"
            ShowPeakHold="true" />

@* ❌ WRONG - Two parameters on the same line *@
<LevelMeter ChannelIndex="ChannelIndex" ShowPeakHold="true" />

@* ✅ CORRECT - Multiple parameters, each on a separate line *@
<ChannelStrip Channel="channel"
              IsSelected="@(selectedIndex == channel.Index)"
              Density="Density"
              ShowSends="true"
              GateThresholdDb="channel.GateThresholdDb"
              AutomixFloorDb="AutomixDepthDb"
              OnSelected="HandleChannelSelectedAsync"
              OnMuteToggled="HandleMuteToggledAsync"
              Class="strip" />

@* ❌ WRONG - Multiple parameters on one line *@
<ChannelStrip Channel="channel" IsSelected="true" Density="Density" ShowSends="true" />

@* ❌ WRONG - Parameters not aligned *@
<ChannelStrip Channel="channel"
IsSelected="true"
Density="Density"
ShowSends="true" />
```

**Rules:**
- **0-1 parameters**: keep on one line
- **2+ parameters**: ONE parameter per line, vertically aligned
- First parameter on the same line as the element name
- All subsequent parameters MUST align vertically
- Space before the self-closing `/>`

---

## Self-Closing Tags

**ALWAYS put a space before `/>`**

```razor
@* ✅ CORRECT - Space before /> *@
<hr class="divider" />
<LevelMeter ChannelIndex="0" />
<input type="range" @bind-value="levelDb" />

@* ❌ WRONG - No space before /> *@
<hr class="divider"/>
<LevelMeter ChannelIndex="0"/>
<input type="range" @bind-value="levelDb"/>

@* ✅ CORRECT - Multi-parameter with /> on the last line *@
<SendRow BusIndex="bus.Index"
         LevelDb="send.LevelDb"
         IsPreFader="send.IsPreFader"
         OnLevelChanged="HandleSendChangedAsync" />

@* ❌ WRONG - /> on its own line *@
<SendRow BusIndex="bus.Index"
         LevelDb="send.LevelDb"
         OnLevelChanged="HandleSendChangedAsync"
/>
```

**Rules:**
- Space before `/>` is MANDATORY
- `/>` stays on the same line as the last parameter
- Never put `/>` on its own line

---

## Child Content Formatting

### Element Content Formatting

**Text content inside elements MUST be on a new line with proper indentation**

```razor
@* ✅ CORRECT - Content on new line *@
<th>
    Device
</th>
<td>
    @device.DriftPpm
</td>

@* ❌ WRONG - Content inline with tags *@
<th>Device</th>
<td>@device.DriftPpm</td>

@* ✅ CORRECT - Table with proper formatting *@
<table class="clocks">
    <thead>
        <tr>
            <th>
                Device
            </th>
            <th>
                Drift ppm
            </th>
        </tr>
    </thead>

    <tbody>
        <tr>
            <td>
                @device.FriendlyName
            </td>
            <td>
                @device.DriftPpm.ToString("F1")
            </td>
        </tr>
    </tbody>
</table>
```

**Rules:**
- Text content goes on a new line after the opening tag
- Closing tag goes on its own line
- Indentation applies to the content
- Applies to ALL elements: HTML tags and VAM components alike

### Empty Lines Between Sibling Elements

**Empty line required between a closing tag and the next opening tag at the same level**

```razor
@* ✅ CORRECT - Empty line between sibling sections *@
</thead>

<tbody>

@* ✅ CORRECT - Empty line between rows *@
<tr>
    <td>
        Jabra Speak 750
    </td>
</tr>

<tr>
    <td>
        Behringer UCA222
    </td>
</tr>

@* ❌ WRONG - No empty line between siblings *@
</thead>
<tbody>

@* ❌ WRONG - No empty line between rows *@
</tr>
<tr>
```

**Rules:**
- Empty line after a closing tag if the next tag is a sibling at the same level
- No empty line between a parent's opening tag and its first child
- No empty line between the last child's closing tag and the parent's closing tag
- This separates logical sections and makes dense markup readable

### Indentation Rules

```razor
@* ✅ CORRECT - 4-space indentation per level *@
<div class="mixtool">
    <button type="button" @onclick="OpenRoutingMatrixAsync">
        Routing matrix
    </button>
    <DensityToggle Density="Density"
                   OnChanged="HandleDensityChangedAsync" />
</div>

@* ❌ WRONG - No indentation *@
<div class="mixtool">
<button type="button" @onclick="OpenRoutingMatrixAsync">Routing matrix</button>
</div>

@* ❌ WRONG - 2-space indentation (use 4) *@
<div class="mixtool">
  <button type="button">Routing matrix</button>
</div>
```

### Closing Tag Alignment

```razor
@* ✅ CORRECT - Closing tag aligned with opening *@
<OverlayPanel IsOpen="isChannelPanelOpen"
              OnClose="CloseChannelPanelAsync">
    <ChainList Links="channel.Chain"
               OnReordered="HandleChainReorderedAsync" />

    <SendList Sends="channel.Sends"
              OnChanged="HandleSendChangedAsync" />
</OverlayPanel>

@* ✅ CORRECT - Deep nesting maintains alignment *@
<div class="strip">
    <div class="meter-fader">
        <LevelMeter ChannelIndex="ChannelIndex" />
        <FaderControl LevelDb="Channel.LevelDb"
                      OnChanged="HandleFaderChangedAsync" />
    </div>
</div>
```

**Rules:**
- 4-space indentation per nesting level
- Closing tag aligned with its opening tag
- Consistent indentation throughout

---

## Code Block Formatting

### @if Statements

```razor
@* ✅ CORRECT - Space after @if, brace on its own line *@
@if (isLoading)
{
    <div class="spinner" />
}
else if (channels.Count == 0)
{
    <p>
        No inputs configured
    </p>
}
else
{
    <p>
        @channels.Count inputs
    </p>
}

@* ❌ WRONG - No space after @if, wrong brace placement *@
@if(isLoading){
<div class="spinner"/>
}else{
<p>No inputs configured</p>
}
```

### @foreach Loops

```razor
@* ✅ CORRECT - Proper @foreach formatting *@
@foreach (ChannelState channel in channels)
{
    <ChannelStrip Channel="channel"
                  IsSelected="@(selectedIndex == channel.Index)"
                  OnSelected="HandleChannelSelectedAsync"
                  @key="channel.Index" />
}

@* ❌ WRONG - No space, poor indentation *@
@foreach(ChannelState channel in channels){
<ChannelStrip Channel="channel"/>
}
```

**Always `@key` a component rendered in a loop.** Without it, reordering strips — which VAM does on drag — makes Blazor reuse the wrong component instances, and a fader jumps to another channel's value.

### @for Loops

```razor
@* ✅ CORRECT - @for with proper spacing *@
@for (int busIndex = 0; busIndex < buses.Count; busIndex++)
{
    <SendRow BusIndex="busIndex"
             LevelDb="sends[busIndex].LevelDb" />
}
```

### @switch Statements

```razor
@* ✅ CORRECT - @switch formatting *@
@switch (device.State)
{
    case DeviceStreamState.Running:
        <span class="pill ok">
            Running
        </span>
        break;
    case DeviceStreamState.Absent:
        <span class="pill bad">
            Absent
        </span>
        break;
    case DeviceStreamState.Faulted:
    default:
        <span class="pill warn">
            Faulted
        </span>
        break;
}
```

**Rules:**
- Space after `@if`, `@foreach`, `@for`, `@switch`
- Opening brace on its own line, aligned with the `@`-directive
- 4-space indentation inside blocks
- Closing brace aligned with the `@`-directive

---

## Attribute Ordering

When an element has multiple attributes, use this logical order:

```razor
@* ✅ CORRECT - Logical attribute order *@
<FaderControl T="float"
              @bind-Value="levelDb"
              Label="Fader"
              MinDb="-60"
              MaxDb="10"
              Disabled="Channel.IsMuted"
              OnChanged="HandleFaderChangedAsync"
              @ref="faderReference"
              Class="fader"
              Style="height: 180px;" />
```

**Suggested order:**
1. **Generic type parameter** (`T=`)
2. **Binding** (`@bind-Value`, `Value`)
3. **Core properties** (`Label`, `Title`, `Text`)
4. **Appearance / range** (`MinDb`, `MaxDb`, `Density`, `Size`)
5. **State** (`Required`, `Disabled`, `IsSelected`)
6. **Events** (`OnChanged`, `OnClick`, `OnSelected`)
7. **Reference** (`@ref`, `@key`)
8. **Styling** (`Class`, `Style`)

**Note:** this is a suggestion, not a strict rule. Consistency within a file matters more.

---

## Spacing Rules

### Blank Lines Between Major Sections

```razor
@* ✅ CORRECT - Blank line between major sections *@
<div class="view">
    <h2>
        Automix
    </h2>

    <ShareHistoryBand Frames="history"
                      Channels="channels" />

    <ChannelShareList Channels="channels"
                      DepthDb="depthDb" />

    <button type="button" @onclick="ToggleAutomixAsync">
        @(isAutomixEnabled ? "Automix on" : "Automix OFF")
    </button>
</div>

@* ❌ WRONG - No spacing (hard to read) *@
<div class="view">
    <h2>Automix</h2>
    <ShareHistoryBand Frames="history" Channels="channels" />
    <ChannelShareList Channels="channels" DepthDb="depthDb" />
</div>
```

### No Blank Lines for Tightly Coupled Elements

```razor
@* ✅ CORRECT - No space between tightly coupled elements *@
<div class="strip-header">
    <span class="strip-name">
        @Channel.Name
    </span>
    <span class="strip-device">
        @Channel.DeviceName
    </span>
</div>

@* ❌ WRONG - Unnecessary blank lines *@
<div class="strip-header">

    <span class="strip-name">@Channel.Name</span>

    <span class="strip-device">@Channel.DeviceName</span>

</div>
```

**Rules:**
- Blank line between major independent sections
- No blank line between tightly coupled elements (parent-child)
- Use judgment for readability

---

## Template Patterns

### RenderFragment Formatting

```razor
@* ✅ CORRECT - Template with proper indentation *@
<DataTable Items="devices">
    <RowTemplate Context="device">
        @if (device.HasDrifted)
        {
            <span class="pill warn">
                @device.DriftPpm.ToString("F0") ppm
            </span>
        }
    </RowTemplate>
</DataTable>

@* ✅ CORRECT - Simple template *@
<DataTable Items="devices">
    <RowTemplate Context="device">
        @device.FriendlyName
    </RowTemplate>
</DataTable>

@* ✅ CORRECT - Multi-element template *@
<DataTable Items="channels">
    <RowTemplate Context="channel">
        <div class="stack">
            <span>
                @channel.Name
            </span>
            <span class="caption">
                @channel.DeviceName
            </span>
        </div>
    </RowTemplate>
</DataTable>
```

### ChildContent Formatting

```razor
@* ✅ CORRECT - ChildContent with proper structure *@
<OverlayPanel IsOpen="isBusPanelOpen"
              Title="Bus">
    <Body>
        <div class="stack">
            <input type="text" @bind-value="bus.Name" />
            <BusChainList Links="bus.Chain" />
        </div>
    </Body>

    <Actions>
        <button type="button" @onclick="CloseBusPanelAsync">
            Cancel
        </button>
        <button type="button" class="primary" @onclick="SaveBusAsync">
            Apply
        </button>
    </Actions>
</OverlayPanel>
```

**Rules:**
- Template content follows the same indentation rules as regular markup
- `RowTemplate`, `ChildContent`, named fragments all indent their children
- Keep templates readable and properly formatted

---

## VAM-Specific: Meters Never Render Through Markup

Level meters, the gain-reduction meter, the automix share band and the drift graph are **canvas elements driven by JS interop from binary frames**. They do not re-render through Blazor.

```razor
@* ✅ CORRECT - the canvas is markup; the pixels are not *@
<canvas @ref="meterCanvas"
        class="meter"
        width="14"
        height="180"></canvas>

@* ❌ WRONG - a meter driven by component state, re-rendered per frame *@
<div class="meter">
    <div class="meter-fill" style="height: @(levelPercent)%"></div>
</div>
```

Diffing the DOM 25 times a second across 16 channels is exactly how this interface would be made to feel slow, and under a WebView it is the single most likely way the mixer view goes wrong.

An empty canvas element is one of the few places where the closing tag stays on the same line — it has no child content, and `<canvas />` is not valid HTML.

---

## Common Mistakes

### Mistake 1: No Space Before />

```razor
@* ❌ WRONG *@
<hr class="divider"/>
<LevelMeter ChannelIndex="0"/>

@* ✅ CORRECT *@
<hr class="divider" />
<LevelMeter ChannelIndex="0" />
```

### Mistake 2: Multiple Parameters on One Line

```razor
@* ❌ WRONG *@
<LevelMeter ChannelIndex="0" ShowPeakHold="true" />

@* ✅ CORRECT *@
<LevelMeter ChannelIndex="0"
            ShowPeakHold="true" />
```

### Mistake 3: Poor Parameter Alignment

```razor
@* ❌ WRONG - Parameters not aligned *@
<ChannelStrip Channel="channel"
IsSelected="true"
Density="Density" />

@* ✅ CORRECT - Parameters vertically aligned *@
<ChannelStrip Channel="channel"
              IsSelected="true"
              Density="Density" />
```

### Mistake 4: Wrong Indentation

```razor
@* ❌ WRONG - 2-space or inconsistent indentation *@
<div class="strip">
  <span>Name</span>
    <button>M</button>
</div>

@* ✅ CORRECT - 4-space consistent indentation *@
<div class="strip">
    <span>
        Name
    </span>
    <button type="button">
        M
    </button>
</div>
```

### Mistake 5: No Space After @-Directives

```razor
@* ❌ WRONG *@
@if(isLoading){
    <div class="spinner"/>
}

@* ✅ CORRECT *@
@if (isLoading)
{
    <div class="spinner" />
}
```

### Mistake 6: Inline Content in Elements

```razor
@* ❌ WRONG - Content inline with tags *@
<th>Device</th>
<td>@device.DriftPpm</td>

@* ✅ CORRECT - Content on new line *@
<th>
    Device
</th>
<td>
    @device.DriftPpm
</td>
```

### Mistake 7: Missing Empty Lines Between Siblings

```razor
@* ❌ WRONG *@
</thead>
<tbody>

@* ✅ CORRECT *@
</thead>

<tbody>
```

### Mistake 8: Missing @key in a Loop

```razor
@* ❌ WRONG - reordering strips reuses the wrong component instances *@
@foreach (ChannelState channel in channels)
{
    <ChannelStrip Channel="channel" />
}

@* ✅ CORRECT *@
@foreach (ChannelState channel in channels)
{
    <ChannelStrip Channel="channel"
                  @key="channel.Index" />
}
```

### Mistake 9: A Destructive Control That Does Not Name Its Target

```razor
@* ❌ WRONG *@
<button type="button" class="danger" @onclick="RemoveInputAsync">
    Remove
</button>

@* ✅ CORRECT - the operator can see what is about to disappear *@
<button type="button" class="danger" @onclick="RemoveInputAsync">
    Remove input @Channel.Name
</button>
```

This applies to every panel, not only the two that prompted the rule.

---

## Touch Events on Clickable Elements

`Vam.Client` is a MAUI Blazor Hybrid host, so its markup runs in a WebView. WebViews — iOS Safari in particular — do not reliably register taps on some elements, which produces the "first tap does nothing" bug.

### Rules for Clickable Elements

**1. Anchor tags (`<a>`) must have an `href` attribute:**

```razor
@* ❌ WRONG - a WebView may not register the click *@
<a @onclick="OpenChannelPanelAsync">
    Open
</a>

@* ✅ CORRECT - empty href with preventDefault *@
<a href="" @onclick="OpenChannelPanelAsync" @onclick:preventDefault>
    Open
</a>
```

**2. Non-semantic clickable elements need `cursor: pointer`:**

A WebView only treats an element as clickable when it has `cursor: pointer`. For a `<div>` or `<span>`:

```razor
@* ❌ WRONG - may not register on a div *@
<div @onclick="SelectChannelAsync">
    @Channel.Name
</div>

@* ✅ CORRECT - add cursor: pointer *@
<div class="clickable" @onclick="SelectChannelAsync">
    @Channel.Name
</div>

@* ✅ BETTER - use a button *@
<button type="button" @onclick="SelectChannelAsync">
    @Channel.Name
</button>
```

**3. Prefer semantic elements:**

- `<button type="button">` for actions — works reliably with no workaround
- `<a href="">` with `@onclick:preventDefault` for navigation-like actions
- Avoid `<div>` and `<span>` for clickable things

VAM's mixer is operated under time pressure, often on a touch screen. A control that needs two taps is a control that gets hit twice during a live meeting.

### Reference

- [GitHub Issue #10725](https://github.com/dotnet/aspnetcore/issues/10725) — Blazor `onclick` on iPhone Safari
- The global `cursor: pointer` rule for `<a>` and `.clickable` lives in `Vam.Ui/wwwroot/css/vam.css`

---

## Quick Reference

```razor
@* ============================================ *@
@* DIRECTIVES *@
@* ============================================ *@

@page "/mixer"
@using Vam.Ui.Components
@inject IVamSession Session
@implements IDisposable

@* ============================================ *@
@* SINGLE-PARAMETER ELEMENTS *@
@* ============================================ *@

<hr class="divider" />
<LevelMeter ChannelIndex="0" />
<button type="button" @onclick="HandleClickAsync">
    Apply
</button>

@* ============================================ *@
@* MULTI-PARAMETER ELEMENTS (2+) *@
@* ONE PARAMETER PER LINE *@
@* ============================================ *@

<LevelMeter ChannelIndex="0"
            ShowPeakHold="true" />

<ChannelStrip Channel="channel"
              IsSelected="true"
              Density="Density"
              OnSelected="HandleSelectedAsync"
              @key="channel.Index" />

@* ============================================ *@
@* CHILD CONTENT *@
@* ============================================ *@

<div class="view">
    <h2>
        Monitors
    </h2>

    <MonitorCard Monitor="monitor" />

    <button type="button" @onclick="AddMonitorAsync">
        Add monitor
    </button>
</div>

@* ============================================ *@
@* CODE BLOCKS *@
@* ============================================ *@

@if (isLoading)
{
    <div class="spinner" />
}

@foreach (ChannelState channel in channels)
{
    <ChannelStrip Channel="channel"
                  @key="channel.Index" />
}

@* ============================================ *@
@* HTML ELEMENTS *@
@* ============================================ *@

<table class="clocks">
    <thead>
        <tr>
            <th>
                Device
            </th>
            <th>
                Drift ppm
            </th>
        </tr>
    </thead>

    <tbody>
        <tr>
            <td>
                @device.FriendlyName
            </td>
            <td>
                @device.DriftPpm.ToString("F1")
            </td>
        </tr>
    </tbody>
</table>

@* ============================================ *@
@* FORMATTING RULES *@
@* ============================================ *@

✅ ALWAYS: Space before />
✅ ALWAYS: 4-space indentation
✅ ALWAYS: Space after @if, @foreach, @for
✅ ALWAYS: One parameter per line (2+)
✅ ALWAYS: Align multi-line parameters
✅ ALWAYS: Content on a new line inside elements
✅ ALWAYS: Empty line between sibling elements
✅ ALWAYS: @key on a component rendered in a loop
✅ ALWAYS: Destructive controls name their target
✅ USE: Blank lines between major sections
❌ NEVER: @code blocks — use a .razor.cs
❌ NEVER: /> on its own line
❌ NEVER: 2-space indentation
❌ NEVER: Two or more parameters on the same line
❌ NEVER: Inline content in elements (<th>Device</th>)
❌ NEVER: Missing empty lines between siblings
❌ NEVER: Meters rendered through component state
```

---

## External Resources

- **[Razor Syntax Reference](https://learn.microsoft.com/en-us/aspnet/core/mvc/views/razor)** — Microsoft Razor documentation
- **[Blazor Syntax](https://learn.microsoft.com/en-us/aspnet/core/blazor/)** — ASP.NET Core Blazor documentation
- **`_MockUp/vam-console.html`** — the design of record. Where markup disagrees with it, the mockup wins unless there is a stated reason
- **`csharp-code-writer` skill** — for `.razor.cs` code-behind formatting and C# standards

---

## Key Rules

**Structure:**
- NEVER use `@code` blocks — every component is `.razor` plus `.razor.cs`
- Every `.razor` file lives in `Vam.Ui`; hosts contribute startup only
- No component library — plain HTML plus VAM's own components

**Spacing:**
- ALWAYS a space before `/>`
- ALWAYS 4-space indentation (never tabs, never 2 spaces)
- Space after `@if`, `@foreach`, `@for`, `@switch`

**Parameters:**
- 0-1 parameters: one line
- 2+ parameters: ONE per line, vertically aligned (CRITICAL)
- `/>` on the same line as the last parameter

**Directives:**
- All at the top of the file
- Order: `@page`, `@using`, `@inject`, `@implements` / `@attribute`
- Blank line after directives before markup

**Code blocks:**
- Opening brace on its own line, aligned with the `@`-directive
- 4-space indentation inside blocks

**Content:**
- Content on a new line, not inline with tags
- Empty line between sibling elements at the same level
- No empty lines between parent and child

**VAM-specific:**
- Meters are canvases driven by binary frames, never component state
- `@key` every component rendered in a loop — strips get reordered
- Destructive controls name their target: "Remove input Mayor 180°", not "Remove"
- Prefer `<button type="button">`; `<a>` needs `href=""` with `@onclick:preventDefault`
