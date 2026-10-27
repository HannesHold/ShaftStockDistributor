# GUI reference

This page lists every control in the desktop window, in the same order as the screen: header, banners, left navigation, then Live demo, Layout, Articles, Optimizer, Scenario study, and Operation.

A **setting** is a value you can edit (text box, slider, check box, combo box, or an editable grid cell). A **button** runs a command. Readouts are listed too, and marked as display only. Values that live on the project file but have no control are collected at the end, so this page does not invent a field that is not on screen.

While a simulation is running or paused, configuration edits stay locked. The header then shows “Stop or reset the simulation to edit the configuration.” Hovering that line shows the same sentence. Disabled buttons still show their tooltip.

## Header

The title bar reads “Shaft Stock Distributor — by Hannes Hold”. Inside the window, the header keeps the short name **Shaft Stock Distributor** next to the HH mark.

### EXAMPLE DATA

Display only. Shown when the open project is the example warehouse.

### Tagline

Display only. “Plan, simulate, and control returnable-crate shafts”.

### Status

Display only. The latest action, such as “Ready”, “Project saved.”, or “Simulation stopped.”

### Configuration lock

Display only, and only while playback is running or paused. Tooltip: “Stop or reset the simulation to edit the configuration.”

### New

Starts a blank project and marks it clean. Disabled while a simulation is running or paused.

Tooltip: “Start a blank project. Disabled while a simulation is running or paused.”

### Open

Asks for a project file and loads it. Disabled while a simulation is running or paused.

Tooltip: “Open a project file. Disabled while a simulation is running or paused.”

### Save

Asks for a path and writes the current project as JSON. Stays available during playback.

Tooltip: “Save the current project as JSON.”

### Load example

Replaces the open project with the example warehouse, builds its assignment, and opens Live demo. If the project has unsaved changes, the load-example banner appears instead of loading immediately. Disabled while a simulation is running or paused.

Tooltip: “Replace this project with the example warehouse. Asks first if there are unsaved changes.”

### Export JSON

Saves the shaft assignment (not the whole project) as JSON.

Tooltip: “Save the shaft assignment as JSON.”

### Export CSV

Saves the shaft assignment as CSV.

Tooltip: “Save the shaft assignment as CSV.”

### Automatic assignment / Manual assignment

The label is the mode that is active now. Pressing it opens the mode banner. It does not switch by itself. Disabled while a simulation is running or paused.

Automatic mode may retarget only empty shafts. Pinned shafts stay. A shaft that still holds crates is never reassigned. Manual mode keeps the stored assignment.

Tooltip: “Ask to switch between automatic and manual assignment.”

### Run simulation

Opens Live demo and starts playback of the project that is already open. It does not load the example. If the stored assignment no longer matches the layout, that same project is optimized again and then played. Disabled while the live page is busy repairing an assignment.

Tooltip: “Open Live demo and play this project. Re-optimizes only if the assignment does not match the layout.”

### Stop

Pauses playback. Same action as Pause on Live demo. Available only while playback is running.

Tooltip: “Pause the simulation. Available only while it is running.”

### Continue

Resumes a paused run from the same time. Same action as Play when playback is already paused. Available only while playback is paused.

Tooltip: “Resume a paused simulation from the same time.”

### Reset simulation

Returns playback to time zero without loading the example. Same action as Reset on Live demo. Available only after playback has left the idle state.

Tooltip: “Return playback to the start. Does not load the example.”

## Mode banner

Shown under the header after Automatic assignment / Manual assignment is pressed.

### Switch assignment mode?

Display only. Title of the banner.

### Body

Display only. “Automatic mode may retarget only empty shafts. Pinned shafts stay as they are. A shaft that still holds crates is never reassigned.”

### Switch mode

Toggles the project between automatic and manual assignment and updates the header button. Disabled while a simulation is running or paused.

Tooltip: “Switch mode. Automatic may retarget empty shafts only. A filled shaft stays.”

### Cancel

Closes the banner and leaves the mode unchanged.

Tooltip: “Keep the current assignment mode.”

## Load-example banner

Shown under the header when Load example is pressed and the project has unsaved changes.

### Load the example project?

Display only. Title of the banner.

### Body

Display only. “The current project has unsaved changes. Loading the example warehouse replaces the layout, articles, and assignment.”

### Load example

Discards the unsaved project, loads the example, and opens Live demo. Disabled while a simulation is running or paused.

Tooltip: “Discard unsaved changes and load the example warehouse.”

### Cancel

Closes the banner and keeps the current project.

Tooltip: “Keep the current project.”

## Navigation

The left rail. The selected page is highlighted. These stay available during playback.

### Live demo

Opens the live aisle.

Tooltip: “Open the live aisle, playback, and shaft assignment.”

### Layout

Opens the rack and shuttle table.

Tooltip: “Open the rack size and shuttle zones.”

### Articles

Opens crate types and the generator.

Tooltip: “Open crate types and the article generator.”

### Optimizer

Opens the assignment search.

Tooltip: “Open the assignment optimizer.”

### Scenario study

Opens the capacity sweep.

Tooltip: “Open the capacity study.”

### Operation

Opens the pull queue and event log.

Tooltip: “Open the pull queue and event log.”

### Checks

Display only, at the bottom of the left rail. “No validation issues.” or a count, then each error and warning from the open project. Errors are things that make a layout or assignment invalid. Warnings include more than five levels, more than nine shafts on a level, and scenario shares that do not add up to 100 percent.

## Live demo

Status under the title repeats the latest playback message. The clock at the right is simulated time.

Chips appear only when they apply: **Shared zone locked** while shuttles contend for an overlap, and **Shuttle waiting** while any shuttle is blocked.

### Pallets / hour

Display only. Steady pallets per hour over the rate window, after warm-up. The caption under the number says “Warming up” or “N min average”.

### Pallets pulled

Display only. Pallets discharged so far. When opening stock was pulled, a second line says that opening stock is included.

### Offered

Display only. Offered pallets per hour from the current article inflows.

### Upper bound

Display only. A travel-time ceiling that ignores shared-zone waiting. Hovering the card shows: “Mean travel between shafts a shuttle can serve, plus the transfer, times the most pallets one pull can discharge. The next pull starts at the last shaft, not at home. Shared-zone waiting is ignored, so this is an upper bound.”

### Utilization

Display only. One chip per shuttle, as a percent of busy time.

### Lost, wait, pulls, blocked

Display only, in the last card. Lost crates, average pull wait, pulls completed against pulls offered, and time spent blocked.

### Aisle

The rack drawing. Click a shaft to select it for the shaft-assignment card. The selection shows as “segment · level · shaft · crates/capacity”. Article colors are listed under the drawing.

Tooltip: “Click a shaft to select it for assignment.”

### Playback

#### Play

Starts playback, or continues it if it is already paused.

Tooltip: “Play from the current time. Continues if playback is already paused.”

#### Pause

Pauses. Available only while playback is running.

Tooltip: “Pause playback. Available only while it is running.”

#### Step

Advances to the next simulation event and leaves playback paused.

Tooltip: “Advance to the next event, then pause.”

#### Reset

Returns to time zero and starts a new session with the current seed. Available only after playback has left idle. Does not load the example.

Tooltip: “Return playback to time zero. Available after play has started.”

#### Pull heatmap

Check box. When on, shaft color shows how often that shaft has been pulled. When off, color is the article color.

Tooltip: “Color shafts by how often they have been pulled.”

#### Speed

Slider from 1 to 400. Each animation tick advances simulated time by about 0.033 seconds times this value. The default is 30. Playback also stops at eight simulated hours. That limit is not a field on this page.

### Simulation

The scenario card stays editable for seed and rate window during playback. The scenario list is disabled while playback is running or paused.

#### Seed

Whole number. Used the next time a session starts (reset, applying a scenario, or loading a project). Changing it does not reseed a run that is already going. Default 1.

#### Rate window (min)

Minutes of history used for the pallets-per-hour figure and the steady-rate chart. Values outside 5 to 180 are pulled back into that range. Default 20.

#### Scenario

Combo box of scenario names on the project. Disabled while playback is running or paused.

#### Apply

Writes that scenario’s total and class shares onto every article’s inflow, remembers the scenario name, and restarts playback at time zero. Disabled while playback is running or paused.

Tooltip: “Write the selected scenario onto article inflows and restart playback.”

### Shaft assignment

The whole card is disabled while playback is running or paused.

#### Selected shaft

Display only. “Click a shaft to assign an article or pin it.” until a shaft is clicked, then the address and fill.

#### Article

Combo box of articles, plus “(unassigned)”.

#### Pin

Check box. A pinned shaft is left alone when automatic mode retargets empty shafts.

Tooltip: “Keep this shaft when automatic mode retargets empty shafts.”

#### Apply to shaft

Writes the chosen article and pin onto the selected shaft. If the shaft still holds crates and the article would change, the article is left as it is and the status says so. When playback is idle, the session restarts. Disabled while playback is running or paused.

Tooltip: “Assign the chosen article and pin to the selected shaft. A filled shaft keeps its article.”

### Steady pallets per hour

Display only. The same rate caption as the first card, plotted against the offered rate.

## Layout

The page title is “Warehouse layout and shuttle motion”. Status reports whether the last edit was kept. The whole editor is disabled while playback is running or paused, and hovering the disabled area repeats the lock sentence.

Nothing on this page is stored until **Apply layout**.

### Shuttles and zones

Hint on the page: each shuttle serves a segment range. Neighbours may overlap. The overlap is shared and uses collision avoidance. A lower-numbered shuttle stays left of the next one, so they never pass. Every segment needs at least one shuttle. Speed defaults to 0.3 m/s.

The table has one row per shuttle. Edited cells are included when you press Apply layout.

#### Shuttle

Display only. “Shuttle 1”, “Shuttle 2”, and so on.

#### From

First segment this shuttle serves, from 1. The column header tooltip is “First segment this shuttle serves”.

#### To

Last segment this shuttle serves. The column header tooltip is “Last segment this shuttle serves”.

#### H speed

Horizontal speed in metres per second. Must be greater than zero and at most 20. Default 0.3. Header tooltip: “Horizontal speed (m/s)”.

#### V speed

Vertical speed in metres per second. Same limits. Default 0.3. Header tooltip: “Vertical speed (m/s)”.

#### H accel

Horizontal acceleration in m/s². Zero uses constant speed. At most 20. Default 0.2. Header tooltip: “Horizontal acceleration (m/s²)”.

#### V accel

Vertical acceleration in m/s². Same limits. Default 0.2. Header tooltip: “Vertical acceleration (m/s²)”.

#### Home X

Home position along the aisle, in metres. Homes must follow aisle order and stay at least the safety distance apart. Header tooltip: “Home position along the aisle (m)”.

#### Home level

Home level, one-based. Header tooltip: “Home level”.

#### Add shuttle

Adds one shuttle, up to the segment count, and splits the aisle evenly, including homes. The table changes immediately. The project changes when you press Apply layout. If the aisle is already full, the status names the limit.

Tooltip: “Add a shuttle and split the aisle evenly. At most one shuttle per segment.”

#### Remove shuttle

Removes one shuttle and splits the aisle evenly. Refuses to go below one shuttle.

Tooltip: “Remove one shuttle and split the aisle evenly. At least one shuttle stays.”

#### Even split

Divides the current segment count across the shuttles already in the table. Neighbouring ranges overlap by about one segment in three of the fleet, and more when the aisle is long relative to the fleet. Homes are spaced along the aisle.

Tooltip: “Divide the segments evenly across the shuttles in the table.”

#### Two-shuttle thirds

Replaces the table with two shuttles. Shuttle 1 runs from segment 1 through `count − count/3`. Shuttle 2 runs from `count/3 + 1` through the last segment, so they share the middle third. Homes go to the two ends of the aisle. Press Apply layout to keep it.

Tooltip: “Set two shuttles that share the middle third. Press Apply layout to keep it.”

### Rack

#### Segments

How many segments are along the aisle. Whole number from 1 to 80. Default on a blank project is 12. A shuttle count above this is rejected.

#### Levels per segment

Levels in each segment. Whole number from 1 to 12. More than 5 is a warning, not a block. Default 5.

#### Shafts per level

Shafts on each level. Whole number from 1 to 20. More than 9 is a warning. Default 9.

#### Shaft length (m)

Flow depth of a shaft, in metres. From 0.5 to 200. Default 20. Together with the crate size and the gap, this sets how many crates fit.

#### Gap between crates (m)

Empty space between crates along the flow. From 0 to 5. Default 0.

#### Shaft pitch (m)

Distance from one shaft to the next along the aisle. Default 0.45.

#### Level height (m)

Vertical distance from one level to the next. Default 0.55.

#### Segment gap (m)

Extra space between neighbouring segments. Default 0.35.

#### Shaft clear width (m, optional)

Clear width used for the cross-section check. Zero or blank stores “not set”, and the check is skipped.

#### Shaft clear height (m, optional)

Clear height for the same check. Zero or blank stores “not set”.

#### Transfer duration (s)

Seconds a shuttle spends handing crates off at a shaft. Default 12. The optimizer’s travel estimate adds this to each hop.

#### Safety distance (m)

Minimum distance between shuttles along the aisle. Default 1.5.

### Apply layout

Validates the rack and every shuttle row, writes them onto the project, rebuilds the shaft grid, and resets playback. If a field is invalid, nothing is written and the status shows the first problem. Checks for range order, coverage, and home spacing still appear in the left-rail list after a successful apply.

Tooltip: “Save the rack and the shuttle table, then reset playback.”

## Articles

The page title is “Crate types”. Add article and Remove stay in the header. The generator, the grid, and the form are disabled while playback is running or paused.

### Add article

Appends an article with a new id (`NEW-` plus a number), the name “New crate”, and class B, then selects it.

Tooltip: “Add a blank article and select it for editing.”

### Remove

Deletes the selected article and clears any shaft that pointed at it.

Tooltip: “Remove the selected article and clear shafts that used it.”

### Generate articles

Hint on the page: each article takes a configured crate type. A movers get a higher demand than B movers, and B higher than C. The shaft assignment is cleared so you can optimize again.

If the project has no crate types, generation uses the standard catalog.

#### Article count

How many articles to create. From 1 to 2000. Default 100.

#### A movers %

Share of the new articles in class A. Default 20.

#### B movers %

Share in class B. Default 30.

#### C movers %

Share in class C. Default 50. A, B, and C must add up to 100. Negative percentages are rejected.

#### Seed (optional)

Whole number for a repeatable catalog. Leave it blank for a new draw. Anything else is rejected.

#### Replace existing articles

Check box. When it is on and the project already has articles, Generate opens the replace banner instead of writing immediately. When it is off, the new articles are appended. Default off.

Tooltip: “Ask before replacing the current article list. Off appends the new articles.”

#### Generate

Validates the fields, then either appends or asks to replace. On success the shaft assignment is cleared. Layout and scenarios stay. Status names how many articles were created in each class.

Tooltip: “Build articles from the crate types. Asks before replacing an existing list.”

#### Replace the article list?

Banner shown only when replace was requested and articles already exist. The body says this removes the current articles and clears the shaft assignment. The layout and scenarios stay.

##### Replace articles

Performs that replacement.

Tooltip: “Replace the article list and clear the shaft assignment.”

##### Cancel

Closes the banner and keeps the current articles.

Tooltip: “Keep the current articles.”

### Article grid

Selecting a row copies it into the form below.

| Column | Editable | Meaning |
| --- | --- | --- |
| Id | No | Article id. |
| Name | No | English name. |
| Class | No | A, B, or C. |
| Inflow | Yes | Crates per hour. A cell edit is kept when you press Apply, even if the form still shows the old number. |
| Pallet | No | Crates per pallet. |
| Offset | No | Extra crates above a full pallet before a pull is requested. |

### Article form

Apply writes this form onto the selected row.

#### Id

Required. Stored without surrounding spaces.

#### Name

Required. Stored without surrounding spaces.

#### ABC class

Combo box: A, B, or C. A is the frequent class.

#### Flow direction

Combo box: Length, Width, or Height. That dimension points along the shaft, front to back.

#### Length (mm)

Crate length. From 1 to 10000. Default for a new draft is 400.

#### Width (mm)

Crate width. From 1 to 10000. Default 300.

#### Height (mm)

Crate height. From 1 to 10000. Default 280.

#### Crates per pallet

Crates in one pallet pull. From 1 to 10000. Default 40. A pull is requested at crates per pallet plus the offset.

#### Offset (crates)

Extra crates above a full pallet before a pull. From 0 to 10000. Default 4.

#### Class weight

Share of that class’s demand given to this article when a scenario is applied. A scenario can override it per article, but that override is not edited here. Default 1.

#### Inflow (crates/h)

Crates per hour for this article. If you changed the form, Apply uses the form. If you only edited the grid, Apply keeps the grid value. Changing inflow restarts playback so arrivals use the new rate. The shaft plan does not move until you run the optimizer.

#### Color

Hex color used in the aisle and the legend, such as `#4C8DFF`. Blank becomes that blue.

#### Apply

Writes the form onto the selected article. Status names the article, and says when inflow changed.

Tooltip: “Write this form onto the selected article. A grid inflow edit is kept, and playback restarts.”

## Optimizer

The page title is “Assignment optimizer”. The form is disabled while playback is running or paused, or while a search is already running. Hovering the disabled form repeats the lock sentence.

### Iterations

How many search steps to run. Negative values are treated as zero. Default 400 on this page (a project file can store another number; opening the page does not overwrite the box from the file until you run).

### Expected pull wait (s)

Target seconds between pulls when the search decides how many shafts an article needs. Default 90. Stored on the project when you run.

### Cost weights

The score is travel hours, imbalance, middle-zone traffic, and a penalty, each multiplied by its weight. Lower is better. Travel is estimated from each shuttle’s home, because the handover pose is not used for planning. A shared shaft is charged to the nearest shuttle that may serve it.

#### Travel

Weight on estimated travel hours. Default 1.

#### Imbalance

Weight on how uneven the shuttle loads are: `(busiest − quietest) / total`. Default 1.2.

#### Middle traffic

Weight on the fraction of pulls that fall in a shared segment. Default 0.8.

#### Penalty

Weight on missing shafts, shafts that can never fill a pull, and addresses outside the layout. Default 4.

### Run optimizer

Stores the iterations, wait, and weights, then replaces the shaft plan with the best plan found. Playback resets. The cards below show the cost before, a naive baseline, the cost after, and how many shafts changed.

Tooltip: “Search for a lower-cost assignment and replace the current plan.”

### Cancel

Shown only while a search is running. Stops it. The assignment already on the project stays.

Tooltip: “Stop the search. The assignment already on the project stays.”

### Cost before

Display only. Cost of the plan that was current when the search started.

### Naive baseline

Display only. Cost of a simple constructive plan.

### Cost after

Display only. Cost of the plan that was kept. During the search this updates with the best cost so far.

### Shafts changed

Display only. How many shaft assignments differ from the plan that was current at the start.

## Scenario study

The page title is “Capacity study”. Status shows progress while a run is going.

### Run study

Sweeps total inflow from the minimum to the maximum in the given step. Each point is simulated for the duration, after the warm-up, and repeated. Results fill the chart, the saturation cards, and the table.

Tooltip: “Sweep the load range and plot achieved pallets per hour.”

### Cancel

Shown only while a study is running. Stops the run. Points already drawn stay.

Tooltip: “Stop the study. Points already finished stay on the chart.”

### Export results CSV

Asks for a path and writes the finished table. Does nothing if no study has finished.

Tooltip: “Save the study table as CSV. Does nothing until a study has finished.”

### Minimum crates/h

Start of the sweep. Loaded from the project. Default 300.

### Maximum crates/h

End of the sweep. Default 4000.

### Step

Gap between loads, in crates per hour. A step below 1 is treated as 1. Default 500.

### Replications

How many times each load is repeated. Below 1 is treated as 1. Default 3.

### Duration (hours)

Simulated hours recorded after warm-up. Below 0.02 is raised to 0.02. Default 2.5.

### Warm-up (hours)

Hours skipped before scoring. Default 1. Zero is allowed.

### Iterations

Optimizer steps used when a point is re-optimized. Default 80 on this page. This is separate from the iterations box on the Optimizer page.

### Achieved / offered threshold

Saturation when achieved pallets per hour fall below this fraction of offered. Default 0.90.

### Utilization ceiling (blank = off)

Optional. A number such as 0.95 marks saturation when any shuttle’s utilization exceeds it. Blank turns the rule off. A value that is not a number is also treated as off.

### Re-optimize each scenario

Check box. When on, each scenario and load is assigned again before it is simulated. Default on. Loaded from the project.

Tooltip: “Assign shafts again for each scenario and load before simulating it.”

### Compare manual and automatic

Check box. When on, each point is run in both assignment modes. Default off.

Tooltip: “Run every load in both manual and automatic assignment.”

### Lost crates mean saturation

Check box. When on, any lost crate marks that load as saturated. Default on.

Tooltip: “Treat any lost crate as saturation.”

### Add scenario

Appends a scenario named “Scenario” plus the next number, with a base total of 1,800 crates per hour and the standard class shares (70 percent A, 20 percent B, 10 percent C). This page does not list scenarios and has no fields for the name, the total, or the shares. Disabled while playback is running or paused.

Tooltip: “Add a scenario at 1,800 crates per hour. This page does not show a list.”

### Remove scenario

Removes the scenario you just added. If you have not added one since the project was loaded, it removes the first scenario. There is no picker. Disabled while playback is running or paused.

Tooltip: “Remove the scenario you just added, or the first one if you have not added one.”

Scenarios are chosen by name on Live demo. Applying one there splits its total across classes, then across articles by class weight.

### Chart

Display only. Solid lines are achieved pallets per hour. Dashed lines in the same color are offered. A pale dashed horizontal line is the upper bound. Red points are loads where saturation was found. Hovering the chart shows the same upper-bound explanation as the Live demo card.

### Saturation cards

Display only. One card per scenario and mode: the highest sustainable pallets per hour, the load where saturation was found (or “not reached in this sweep”), and the reason.

### Results table

Display only.

| Column | Meaning |
| --- | --- |
| Scenario | Scenario name. |
| Mode | Manual or automatic. |
| Load | Sweep load, crates per hour. |
| Offered | Offered pallets per hour. |
| Achieved | Achieved pallets per hour. |
| Utilization | One percent per shuttle. |
| Lost | Lost crates. |
| Blocked | Seconds shuttles spent blocked. |

## Operation

The page title is “Live operation”. Nothing here is editable. It follows the live simulation.

### Mode hint

Display only. The same sentence as the mode banner: automatic mode may retarget only empty shafts.

### Shuttle summary

Display only. One phrase per shuttle: number, phase, and level.

### Shared zone locked / Shuttle waiting

Display only. A chip when an overlap is locked or a shuttle is waiting. The chip text follows whichever of those is true.

### Pull queue

Display only.

| Column | Meaning |
| --- | --- |
| Job | Job id. |
| Article | Article being pulled. |
| Location | Shaft address. |
| State | Queue state. |
| Shuttle | Shuttle that owns the job, when one does. |
| Crates | Crates in the pull. |

### Event log

Display only. Recent simulation lines, newest as produced by the session.

### Article cards

Display only. One card per article: color, name, class, inflow in crates per hour, and lost crates.

## Stored on the project, not shown

These values are real and are kept when you save. No control edits them. They are listed so a missing box is not mistaken for a forgotten setting.

| Value | Where it matters | What the window does with it |
| --- | --- | --- |
| Handover X and handover level | Each shuttle | Loaded with the shuttle and written back by Apply layout. The shuttle table does not show them. Travel back to the handover pose is not simulated. |
| Optimizer seed and cooling | Optimizer search | Used by the search. Cooling defaults to 0.997. The Optimizer page does not show them, and Run optimizer does not change them. |
| Study base seed | Scenario study | The sweep’s random seed. The study page does not show it. |
| Scenario name, base total, class shares, per-article weights | Live demo scenario list, and Apply there | Add scenario creates a name and 1,800 crates per hour with shares 0.70 / 0.20 / 0.10. Nothing on Scenario study edits those numbers afterward. Shares that do not add up to 100 percent are scaled when the scenario is applied, and Checks warns. |
| Reassignment cooldown, hysteresis, max reassignments per hour, observation window | Automatic mode | Defaults are 180 seconds, 0.25, 24 per hour, and 600 seconds. Operation does not show them. |
| Eight-hour playback horizon | Live demo | Playback pauses at eight simulated hours. There is no field for it. |

## Count

| Kind | Count |
| --- | --- |
| Editable settings (text boxes, slider, check boxes, combo boxes, editable grid columns) | 64 |
| Buttons, including banner and navigation buttons | 45 |
| Click target that is not a button | 1 (the aisle) |

Every button has a tooltip, and so does the aisle. The six check boxes are settings, and they have tooltips too, because a check box is a pressable control. Disabled buttons still show theirs. Readouts and the stored-but-hidden values above are documented and are not part of the 64.
