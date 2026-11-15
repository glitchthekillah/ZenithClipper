# Premiere Pro parity plan

Source: Adobe's Premiere features page and the full Premiere user guide table of contents (helpx.adobe.com/premiere-pro/user-guide.html), Nov 2026.
Excluded on purpose: generative AI (Generative Extend, Generate on timeline, Object Mask, Media Intelligence search, Color Match AI, Auto Reframe AI, Enhance Speech AI, AI tagging, Text-Based Editing transcription), Adobe Stock, Frame.io, Team Projects, Creative Cloud Libraries, mobile.

Status (updated Nov 9): [x] done · [~] partial · [ ] to build. Engine column: how it maps onto MLT/ffmpeg.

## 1. Timeline editing and tools
| Feature | Status | Engine |
|---|---|---|
| Selection tool (V), drag clips between compatible tracks, overwrite on drop | [x] | edit model |
| Linked A/V move together, mirrored V/A lanes, divider | [x] | edit model |
| Snapping (S) to edits, playhead, markers | [x] | edits, playhead, markers, In/Out |
| Razor tool (C), add edit at playhead (Ctrl+K), add edit to all tracks (Ctrl+Shift+K) | [x] split only | Split |
| Trim clip edges by dragging (head/tail) | [x] | Trim |
| Ripple edit tool (B), ripple trim to playhead (Q / W) | [x] | Trim + shift |
| Rolling edit tool (N) | [x] model exists | Roll |
| Slip tool (Y) | [x] model exists | Slip |
| Slide tool (U) | [x] | Move + Roll |
| Rate Stretch tool (R) | [x] model exists | RateStretch |
| Track Select Forward/Backward (A / Shift+A) | [x] | UI |
| Hand (H) / Zoom (Z) tools | [x] wheel only | UI |
| Ripple delete (Shift+Del), delete gap | [x] clip only | Delete |
| Lift (;) / Extract (') between In/Out | [x] | Clear + ripple |
| Insert (,) / Overwrite (.) from source with In/Out and source patching | [x] | Paste(insert) at program In or playhead; targets selected V/A track |
| Copy, cut, paste, paste insert (Ctrl+Shift+V), paste attributes (Ctrl+Alt+V) | [x] | model |
| Duplicate (Alt+drag), nudge clip (Alt+Left/Right), move to track (Alt+Up/Down) | [x] | Move / Paste |
| Enable/disable clip (Shift+E) | [x] model flag | Enabled |
| Group / ungroup clips (Ctrl+G) | [x] | Shift/Ctrl+click multi-select; groups move, nudge, copy, nest and delete together |
| Link / unlink (Ctrl+L) | [x] | |
| J/K/L shuttle, frame step, go to next/previous edit (Up/Down) | [x] | transport |
| Go to In/Out, mark In/Out (I/O), clear (Ctrl+Shift+X) | [x] | |
| Zoom to sequence (\\), timeline zoom (+/-), Ctrl+scroll | [x] | UI |
| Track heights, expand/collapse tracks | [x] | Ctrl+= / Ctrl+- all tracks |
| Track targeting and source patching | [x] | click V1/A1 in the track header |
| Sync lock / track lock | [x] | sync lock on ripple delete |
| Nested sequences, multiple sequences, sequence tabs | [x] | tractor in tractor, transparent background; Nest, double-click to open, cycle check |
| Multi-camera source sequence + switching | [x] | nested, 1-9 cuts at playhead |
| Freeze frame (Add Frame Hold), Insert Frame Hold Segment | [x] | still producer |
| Speed/Duration dialog, reverse, maintain pitch, ripple on speed change | [x] | |
| Time remapping (speed ramps, keyframed) | [x] | constant-speed runs, one timewarp per distinct speed (≤ ~16) |
| Scene edit detection | [x] | ffmpeg select=scene, cuts linked clips |
| History panel | [x] | |
| Markers: sequence + clip, colors, names, notes, jump (Shift+M) | [x] add only | |

## 2. Monitors
| Source monitor (load clip, In/Out, insert/overwrite) | [x] | Source/Program tabs share the preview; double-click media |
| Program monitor zoom/fit, playback resolution (Full/1/2/1/4) | [x] | Fit, 10-400% with scrolling; playback resolution |
| Safe margins, guides, rulers | [x] | safe margins; rulers; guides dragged from rulers, saved per sequence |
| Direct manipulation: move/scale/rotate on the monitor | [x] | box handles, rotate outside corners, Shift snaps, keyframe-aware |
| Crop handles on the monitor | [x] |
| Comparison view (before/after) | [x] | reference frame with draggable split; FX mute |
| Dropped frame indicator, timecode overlay | [x] | |
| Lumetri scopes: waveform, vectorscope, histogram, parade | [x] | ffmpeg waveform/vectorscope filters |
| Full screen playback, second monitor | [x] | full screen; Program on second display; floating panels |

## 3. Motion / transform / compositing
| Position, scale (uniform/non-uniform), rotation, anchor point | [x] | affine/qtblend via shared TransformGeometry |
| Opacity + blend modes (normal, multiply, screen, overlay, add, darken, lighten, difference…) | [x] opacity only | qtblend/frei0r |
| Crop (left/right/top/bottom, feather, zoom to fill) | [x] building first | crop filter |
| Flip horizontal/vertical | [x] | avfilter.hflip/vflip |
| Masks: rectangle, ellipse, pen, feather, expansion, invert, tracking | [x] | luma shape; tracked ellipse/rectangle via OpenCV KCF in a helper process + alphaspot |
| Adjustment layers | [x] | effects planted on the tractor at the layer's place in the stack |
| Keyframes on every property, bezier/ease, graph editor, copy/paste keyframes | [x] | Linear, Hold, Bezier, Ease In/Out; value graph with drag and right-click |
| Warp Stabilizer | [x] | vidstab (bundled) |
| Ultra Key / chroma key, luma key | [x] | avfilter.chromakey |

## 4. Transitions
| Cross dissolve, dip to black/white, film dissolve, additive dissolve | [x] | luma/mix |
| Wipes (linear, radial, clock, barn door, iris), push, slide, split, zoom, spin | [x] | luma + composite |
| Default transition (Ctrl+D, Ctrl+Shift+D), transition duration, alignment (center/start/end) | [x] | |
| Morph cut | [ ] | AI-adjacent; skip |
| Audio crossfades: constant power, constant gain, exponential | [x] | mix |

## 5. Video effects library
| Blur/sharpen: gaussian, directional, sharpen, unsharp mask | [x] box blur | avfilter |
| Distort: lens distortion, mirror, offset, transform, turbulent displace | [x] | lens, mirror, offset, corner pin, wave, wave warp |
| Generate: color matte, gradient, black video, bars and tone, transparent video, counting leader | [x] | no tone, no leader |
| Image control: black & white, color pass, color replace, gamma | [x] gamma | avfilter |
| Noise & grain: noise, median, dust & scratches | [x] | avfilter |
| Perspective: drop shadow, bevel, basic 3D | [x] | shadow = silhouette copy under the clip; drawbox bevel; perspective warp |
| Stylize: glow, mosaic, posterize, find edges, strobe, emboss | [x] | avfilter |
| Time: echo, posterize time | [x] | tmix, choppy |
| Vignette, film grain, letterbox | [x] | |
| Effect presets, copy/paste effects, effect on/off toggle (fx badge) | [x] | |

## 6. Color (Lumetri)
| Basic: white balance (temp/tint), exposure, contrast, highlights, shadows, whites, blacks, saturation, vibrance | [x] | avfilter.eq/colortemperature/curves |
| Creative: look LUT, intensity, faded film, sharpen, vibrance, shadow/highlight tint | [x] | lut3d |
| Curves: RGB curves, hue vs sat, hue vs hue, hue vs luma, luma vs sat | [x] | curves/huesaturation |
| Color wheels (shadows/midtones/highlights) | [x] | colorbalance |
| HSL secondary | [x] | huesaturation by color range |
| Vignette | [x] | vignette |
| LUT import (.cube) | [x] | lut3d |
| Auto color | [x] | normalize |

## 7. Audio
| Clip gain, volume keyframes / rubber band on clips | [x] gain | volume |
| Pan / balance | [x] | panner |
| Audio Track Mixer: faders, pan, mute/solo, meters | [x] | |
| Audio Clip Mixer | [x] | clips under playhead |
| Effects: parametric EQ, compressor, limiter, de-esser, noise reduction, reverb, delay, pitch shift, high/low pass, hard limiter | [x] | avfilter |
| Essential Sound: dialogue/music/SFX presets, loudness auto-match, ducking | [x] | |
| Loudness meter (LUFS) | [x] | own BS.1770 meter, matches ffmpeg ebur128; Match loudness to -14/-16/-23/-24 |
| Audio meters on the monitor | [x] | |
| Record voice-over to a track | [x] | ffmpeg dshow, Shift+V |
| Solo track, track gain | [x] mute only | |
| Audio waveforms on clips | [x] | |
| Normalize, channel mapping, mono/stereo, swap channels | [x] | |
| Audio crossfades | [x] | |

## 8. Titles, graphics, captions
| Text: font, size, color, alignment, tracking, leading | [x] | rasterized |
| Stroke, shadow, background box, gradient fill | [x] | |
| Shapes: rectangle, ellipse, pen; align and distribute | [x] | shapes; pen masks (straight/smooth); align and distribute |
| Text styles, track styles | [x] | saved text styles + caption style |
| Credit rolls / crawls | [x] | roll |
| Captions: auto (local whisper), SRT import/export, styles, burn-in | [x] / [~] styles | |
| Single word captions, caption track styling | [x] | |
| Find and replace, spell check | [x] | Windows spell checker (COM), user dictionary |

## 9. Project and media
| Bins/folders, list and icon views, sort, search | [x] | bins, list/icon view, sort, search, rename |
| Subclips | [x] | Ctrl+U in Source |
| Metadata panel / clip info | [x] | inspector Source info |
| Relink offline media | [x] | |
| Proxies (create, attach, toggle) | [x] | 540p H.264 |
| Interpret footage (frame rate, field order, alpha) | [x] | frame rate, field order, alpha ignore/invert/premultiplied |
| Image sequences, PSD/AI layers | [~] | image sequences; PSD layers as sequence (8-bit RGB); AI not supported |
| Project manager (consolidate, collect files) | [x] | collect files |
| Auto save | [x] | |
| Multiple projects open | [x] | one window per project; copy/paste between them |
| Import XML (FCP X, Premiere) | [x] | FCP7 XML import + export (cuts, tracks, links) |

## 10. Export
| Export presets (YouTube 1080p/4K, Shorts/TikTok vertical, Discord 8/25/50 MB target size) | [x] | |
| Format, codec (H.264, HEVC, AV1, ProRes), hardware encoding, bitrate CBR/VBR, two-pass | [x] | avformat consumer |
| Range: entire sequence, In/Out, work area | [x] | |
| Frame size, frame rate, aspect, scaling | [x] | |
| Audio codec, bitrate, sample rate | [x] | |
| Export frame (Ctrl+Shift+E) to PNG/JPG | [x] | |
| Export captions sidecar, burn-in | [x] | burn in, .srt or .vtt sidecar, or none |
| Export queue | [x] | |
| Render previews (in-to-out), smart rendering | [x] | Render In to Out (Enter), render bar; Smart export copies an untouched clip range without re-encoding (keyframe start) |

## 11. Workspace and preferences
| Customizable keyboard shortcuts (Premiere preset) | [x] | |
| Panels: resize, dock, workspaces (Editing, Color, Audio, Effects) | [x] | workspaces; drag Mixer, Scopes, History between panel groups or out to float |
| Preferences: auto save, playback, timeline, trim, labels | [x] | autosave, durations, track height, pre/post-roll (Shift+K), large trim offset |
| Label colors on clips | [x] | |
| Info panel, Effects panel with search | [x] | |

## Build order
1. Crop, flip, edge trimming, tool palette (V C B N Y U R A H Z), J/K/L, I/O, markers, copy/paste, nudge, Q/W, add edit all tracks.
2. Transitions (video + audio), default transition, fades.
3. Lumetri basic + creative + curves + wheels + LUTs, scopes.
4. Audio: pan, track mixer with meters, EQ/compressor/limiter/noise reduction/reverb, loudness normalize, ducking, crossfades.
5. Keyframes everywhere + graph editor, blend modes, masks, adjustment layers, chroma key, stabilizer.
6. Titles 2.0 (stroke/shadow/gradient/shapes/styles/credits), caption styles.
7. Export presets + target size + range + frame export + queue.
8. Sequences: nesting, multiple sequences, multicam, freeze frames, time remapping, scene detection, subclips, bins, proxies, source monitor.
9. Workspaces, shortcut editor, preferences, label colors.


## Done in the Nov 9 pass
Tools V A B N C Y U R H Z with edge trimming; Premiere shortcuts (I/O, X, J/K/L, Q/W, Ctrl+K, Ctrl+Shift+K, ; and ', Up/Down, Alt+arrows, M/Shift+M, Shift+E, Ctrl+C/X/V, Ctrl+Shift+V, Ctrl+Alt+V, Ctrl+D/Ctrl+Shift+D/Shift+D, Ctrl+L, Ctrl+Shift+E, S, \\, =/-); 29 video transitions + 3 audio crossfades (handles, centre/end/start alignment); 48 video and audio effects including Lumetri basic/wheels/curves/HSL/LUT; frame hold and frame-hold segment; label colors; solo; export presets with target size (Discord), In/Out range, H.265/AV1/ProRes/NVENC, audio-only; export frame. Every engine feature was rendered with melt and checked.

## Done in the second Nov 9 pass
Keyframes (position, scale, rotation, opacity, volume) with ◇ toggles and prev/next navigation; keyframes survive split/trim/roll/slide/stretch; 18 blend modes; ellipse/rectangle masks with feather and invert; Audio Track Mixer (volume, pan, mute, solo) with master and transport meters; Lumetri-style scopes (waveform, RGB parade, vectorscope, histogram). Still open: mask tracking and pen masks, graph editor (keyframes are linear), bezier keyframe handles.

## Done in the Nov 10 pass
Anchor point; program monitor zoom; multiple project windows with cross-project paste; Drop Shadow, Bevel Edges, Basic 3D; Corner Pin now leaves the outside transparent; field order; Program on second display; Align and Distribute; Comparison view; pen masks; spell check; PSD layers as a sequence; Render In to Out with render bar; floating panels. 154 render scenarios clean.

## Done in the second Nov 10 pass
Rulers and guides; list/icon view and sort in the Project panel; alpha interpretation; caption export as burn-in, .srt or .vtt sidecar; play around with pre/post-roll; large trim offset preference. 157 render scenarios clean.
Left on purpose: Morph Cut (AI-style face tracking), Illustrator layers, smart rendering, free drag-docking of every panel.

## Done in the third Nov 10 pass
Keyframe easing (Ease In/Out/In-Out) and a value graph editor; transform keyframes now honour non-linear interpolation (were linearised before); mask tracking; drag-docking panels between groups. 159 render scenarios clean.

Smart export (stream copy for untouched clip ranges) added in the fourth Nov 10 pass.
