# Native T-FLEX live preview

The editor defaults to a crop of the native plan from the PDF produced by
TFlexAutomationRunner from a copied GRB with the current form parameters. The
preview switch also offers a fast schematic SVG built from current parameters.
SVG is approximate and available only for the nine supported templates; PDF
uses the native CAD export used for generated drawings.

## Request and publication

The browser waits 900 ms after an edit, classifies the current parameters, checks
worker readiness, and posts `/api/jobs` with `isPreview: true` and PDF output.
Hard errors and deviations requiring engineer confirmation stop automatic preview.
The existing native worker processes the job; no second CAD service is required.

Any parameter or template change clears the displayed PDF. Only a result matching
the current form revision can be published. Temporary queue saturation is retried
for up to ten minutes; a retry button can request the same parameters again.
Native export takes time, so PDF preview is asynchronous. Switching temporarily
to SVG keeps a completed PDF cached for the same parameter snapshot. Editing
parameters while SVG is selected clears that cache and does not start a PDF job.

Preview jobs use the existing authentication, ownership and file access rules.
They are omitted from ordinary job history. Matching pending/running jobs are
reused, completed jobs are regenerated, and superseded pending previews are
cancelled atomically. At most one preview per owner runs at a time. Existing
queue limits and finished-job retention still apply.

## Plan crop and full sheet

| Templates | Initial PDF page | Native page count |
| --- | --- | --- |
| UN-Victor MRL, MRL T, R | 2: shaft plan and load points | 5 |
| LEHY-L-PRO, both ranges | 2: shaft plan | 5 |
| LEHY-PRO, side and rear CWT | 2: shaft/pit plans | 6 |
| K-II-TYPE | 2: escalator general view | 5 |
| Развертки LEHY | 1: cabin layout | 1 |

The embedded preview renders only the main plan with its dimension lines.
Template-specific normalized crop bounds omit the sheet frame, title block,
notes and secondary views. K-II shows its general escalator profile; cabin
unfolding shows the cabin plan. Bounds were checked against all nine native
reference PDFs. Changes to source page layouts require rechecking these bounds.

The “Открыть весь лист” button opens the full PDF in a separate local canvas
viewer, with page navigation and zoom. The embedded crop retains
zoom controls and does not offer full-sheet/page navigation. Both views use
vendored Mozilla PDF.js 6.4.299; rendering does not depend on browser PDF plugins.

## Source parameter calculations

`calculatedVariables` take precedence over duplicate read-only parameter
expressions. This preserves K-II manual TK/TJ values alongside calculated TG.
Legacy shaft-door A4 helpers only apply when `$door_type` exists; cabin unfolding
retains its own A4 formula. The editor and pricing configuration resolver use the
same precedence. Unavailable values such as `-1` remain unavailable.

## Native reference verification

On 2026-10-04 all nine server templates were successfully exported using the
installed licensed T-FLEX runtime on alesnichiy.ru. These reference exports use
the parameter state saved in each source GRB, not browser catalog defaults.
Their initial drawing sheets were rendered and visually inspected. In an isolated
Windows API/Worker instance, changing UN-Victor MRL shaft width from 2750 to
3000 mm through the browser produced a completed five-page PDF. Visual inspection
confirmed HW 3000 on the shaft and pit plans. The preview was absent from ordinary
job history, and editing the width cleared the previous document immediately.

`scripts/Export-TFlexPreviewReferences.ps1` reproduces isolated native reference
exports on Windows. It copies source fragments, refuses existing output folders,
validates PDF signatures and emits source hashes in a manifest. It never edits
source drawings.

## Runtime acceptance, 2026-10-04

All nine templates completed native PDF preview jobs on the isolated Windows
candidate API and ExternalProcess Worker. Crops were visually checked against
those PDFs. Browser verification on port 5012 confirmed the XIZI MRL plan with
HW 3000, cropped inline display, zoom controls, and the separate full-sheet
viewer with page navigation. Served viewer and PDF.js assets matched local
SHA-256 hashes. Production services were not updated.

The SVG/PDF switch was verified in the same candidate browser. SVG plans were
displayed for all nine templates. Returning to a completed PDF with unchanged
parameters reused its URL. Changing XIZI MRL shaft width from 3000 to 3100 mm
updated SVG immediately, cleared the previous PDF link, and produced a new
native PDF whose plan showed HW 3100. The nine served preview assets matched
the local files, and the frontend suite passed 126 tests.

## Cabin unfolding SVG, 2026-10-05

The plan shows panel seams and separate dimension chains for walls A (rear),
B (right), C (left), and D (front). It uses the catalog's native panel formulas.
For the 1100 × 2100 baseline, the B plan chain is 700 / 767.5 / 221 / 411.5 mm;
the elevation chain is 693 / 760.5 / 235 / 411.5 mm. The COP strip is WB+1 in
plan and WB+15 in elevation. These two chains must not be interchanged.

Front COP uses ordinary side-wall panel chains and the centre of the front
door flank. Through side-COP layouts mirror the C plan chain; native CO
elevations keep the opening centred even when A4 offsets the top plan.
The default, front-COP and through-CO chains were checked against isolated
exports from the installed server T-FLEX runtime. Source GRBs were copied
to a temporary directory before export.

The SVG prioritizes readable panel positions and dimensions: its plan is
full width, with roof/floor and elevations below it in the preview's scroll
area. Ceiling drawings remain schematic; ND10 controls use native dimensions,
while other COP models show their panel width and position. PDF remains the
authoritative drawing for finish details and model-specific controls.
