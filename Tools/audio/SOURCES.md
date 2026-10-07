# Starter SFX sources

The clips live in `DestructionPOC/Assets/Destruction/Resources/Audio/` (paths below are relative to it). They are cut from the Sonniss "GDC Game Audio Bundle" archives in `~/Documents/sfx`. The bundles are royalty-free for commercial use, and attribution is not required.

Processing:

- Format is 48 kHz, 16-bit WAV.
- One-shots are mono so that Unity can position them in 3D. Each has a 4 ms fade-in and a cosine fade-out, and is peak-normalised to -1 dBFS.
- `*_loop` clips loop seamlessly: the tail is crossfaded into the head with equal power. They are peak-normalised to -3 dBFS.

How the game plays them:

- `Sfx` loads every clip and groups numbered variants into banks: `concrete_hit_01`…`_05` are the bank `concrete_hit`.
- `MachineAudio` runs the engine sets. The heavy set is used for the excavator, dozer, wheel loader and crane, and the light set for the skid steer. The idle and load loops crossfade by throttle.
- `DestructionAudio` plays breaks, impacts and collapses.
- `horn_excavator`, `glass_break`, `collapse_medium_glass`, `hydraulic_strain_loop`, `container_scrap_drop` and `ui_click_mechanical` are not used yet: the game has no horn, breakable glass or matching event for them.

| Clip | Library | Source file | Range (s) |
|---|---|---|---|
| `Machines/engine_heavy_start.wav` | Pole Position - Volvo L90c Wheel Loader 1996 | Volvo_L90_Wheel_Loader_t1_Ext_Start_Slow_Away_Up_Reverse_Stop_Away_By_Up_Stop_Off_XY_RSM191.wav | 0.85–6 |
| `Machines/engine_heavy_idle_loop.wav` | Pole Position - Volvo L90c Wheel Loader 1996 | Volvo_L90_Wheel_Loader_t1_Ext_Start_Slow_Away_Up_Reverse_Stop_Away_By_Up_Stop_Off_XY_RSM191.wav | 2.5–11.5 (loop) |
| `Machines/engine_heavy_load_loop.wav` | Pole Position - Volvo L90c Wheel Loader 1996 | Volvo_L90_Wheel_Loader_t4_Onbrd_Drive_Gearshifts_Stop_Off_Engine_Right_RE50.wav | 11–18.2 (loop) |
| `Machines/engine_heavy_stop.wav` | Pole Position - Volvo L90c Wheel Loader 1996 | Volvo_L90_Wheel_Loader_t1_Ext_Start_Slow_Away_Up_Reverse_Stop_Away_By_Up_Stop_Off_XY_RSM191.wav | 467–472.5 |
| `Machines/engine_light_start.wav` | SoundHolder - Tractor Case II CX90 | tractor Case II CX90 exterior engine on idle and off stereo M10.wav | 0.78–3 |
| `Machines/engine_light_idle_loop.wav` | SoundHolder - Tractor Case II CX90 | tractor Case II CX90 exterior engine on idle and off stereo M10.wav | 2–12.8 (loop) |
| `Machines/engine_light_stop.wav` | SoundHolder - Tractor Case II CX90 | tractor Case II CX90 exterior engine on idle and off stereo M10.wav | 12.8–16.2 |
| `Machines/tracks_clank_loop.wav` | Pole Position - Sherman M4A3 Medium Tank | sherman_m4a3_t4_onbrd_medium_drive_steady_slow_short_tracks_right_RE50.wav | 28–38.5 (loop) |
| `Machines/hydraulic_move_loop.wav` | Pole Position - Chaffee M24 Light Tank | chaffee_m24_t7_var_sfx_ext_hydraulic_traverse_XY_RSM191.wav | 24–29.2 (loop) |
| `Machines/hydraulic_strain_loop.wav` | Pole Position - Hydraulics & Pneumatics Library | Machine_6_mono_06.wav | 1.5–9.2 (loop) |
| `Machines/hydraulic_stop_clunk.wav` | Pole Position - Hydraulics & Pneumatics Library | Machine_6_mono_06.wav | 9.35–11.4 |
| `Machines/hydraulic_release_01.wav` | Justsoundeffects - Industrial Robot | MECHHydr_Hydraulic Pressure Releasing_JSE_IR_01_Stereo.wav | 1–1.85 |
| `Machines/hydraulic_release_02.wav` | Justsoundeffects - Industrial Robot | MECHHydr_Hydraulic Pressure Releasing_JSE_IR_01_Stereo.wav | 2.55–3.5 |
| `Machines/hydraulic_release_03.wav` | Justsoundeffects - Industrial Robot | MECHHydr_Hydraulic Pressure Releasing_JSE_IR_01_Stereo.wav | 4.35–5.2 |
| `Machines/horn_excavator.wav` | SoundHolder - Excavator MF 860 | excavator MF 860 exterior horn stereo M10.wav | 1.3–3.6 |
| `Machines/crane_winch_loop.wav` | Ivo Vicic - Industrial hall | 03 Industrial hall overhead crane_ lifting crane hook operation_fast_crane departure.wav | 18–31 (loop) |
| `Machines/chain_rattle_01.wav` | InspectorJ - Essentials 04 Chains | CHAINMvmt_InsJ_Chains_Metal_Movement_Distant_Moderate_02-01.wav | 0–2 |
| `Machines/chain_rattle_02.wav` | Soundopolis - Heavy Metal! | Chain Movement_On Metal_Variety_Fienup_001.wav | 3.4–6.4 |
| `Machines/wrecking_ball_swing_01.wav` | Soundholder - Swipes And Whooshes | swipes and whooshes metal grille slow and long swings stereo ORTF 8040.wav | 1.2–1.8 |
| `Machines/wrecking_ball_swing_02.wav` | Soundholder - Swipes And Whooshes | swipes and whooshes broom long stick slow and long swings stereo ORTF 8040.wav | 2.05–2.7 |
| `Machines/bucket_scoop_dirt.wav` | Mechanical Wave - Excavator Sounds | VEHCnst_Excavator Bucket Shake_MWSFX_EXS 03.wav | 0–4 |
| `Machines/bucket_dump_earth.wav` | Mechanical Wave - Excavator Sounds | VEHCnst_Bucket Pour Earth_MWSFX_EXS 07.wav | 0–3.52 |
| `Machines/container_rubble_dump.wav` | RDGSFX008 - The Metal Shelf | Falling Rocks on Metal 04.wav | 0.17–5.6 |
| `Machines/container_scrap_drop.wav` | Double Trouble Audio - Junk and Debris | Pile of Scrap Metal - Metal Scrap Pile, Rustle, Drop, Long Tail 03.wav | 0.45–3.4 |
| `Destruction/concrete_impact_heavy_01.wav` | SoundMorph - RUPTURE | Rupture - Concrete_Impact_B02.wav | 0–3 |
| `Destruction/concrete_impact_heavy_02.wav` | PMSFX - Shattering Bricks | PM_SB_DESIGNED_IMPACT_48 Impact brick rock dirt gravel designed multi LFE.wav | 0–2.28 |
| `Destruction/concrete_impact_heavy_03.wav` | PMSFX - Shattering Bricks | PM_SB_DESIGNED_IMPACT_116 Impact brick rock dirt gravel designed multi LFE.wav | 0–2.4 |
| `Destruction/concrete_hit_01.wav` | Airborne Sound - Half & Half Sound FX Pack 2 | Work,Hammer,Wall,Concrete Block,Sledgehammers,Pair,Hit,Debris,Patient - very faint background construction.wav | 44.6–45.5 |
| `Destruction/concrete_hit_02.wav` | Airborne Sound - Half & Half Sound FX Pack 2 | (same file) | 46.03–47 |
| `Destruction/concrete_hit_03.wav` | Airborne Sound - Half & Half Sound FX Pack 2 | (same file) | 51.22–52.2 |
| `Destruction/concrete_hit_04.wav` | Airborne Sound - Half & Half Sound FX Pack 2 | (same file) | 52.4–53.4 |
| `Destruction/concrete_hit_05.wav` | Airborne Sound - Half & Half Sound FX Pack 2 | (same file) | 53.59–54.6 |
| `Destruction/brick_impact_01.wav` | PMSFX - Shattering Bricks | PM_SB_SOURCE_16 Impact brick rock dirt gravel single hit.wav | 0–1.2 |
| `Destruction/brick_impact_02.wav` | PMSFX - Shattering Bricks | PM_SB_SOURCE_61 Impact brick rock dirt gravel single hit.wav | 0–1.5 |
| `Destruction/brick_impact_03.wav` | Soundrangers - Foley Elements Rocks, Dirt And Stone | brick_impact_debris_02.wav | 0–0.84 |
| `Destruction/brick_impact_04.wav` | Mechanical Wave - Rock Brick and Dirt | Debris Brick_RBD 02.wav | 0–1.22 |
| `Destruction/chunk_land_01.wav` | Mattia Cellotto - Rocks Momentum | Collapsed Stone House - Rock Single Large Impacts - 1 Meter. Rock,Large,Impact,Single,Land,Various_04.wav | 0.05–1.6 |
| `Destruction/chunk_land_02.wav` | Mattia Cellotto - Rocks Momentum | (same file) | 8.55–10.1 |
| `Destruction/chunk_land_03.wav` | Mattia Cellotto - Rocks Momentum | (same file) | 10.27–11.8 |
| `Destruction/chunk_land_04.wav` | Mattia Cellotto - Rocks Momentum | (same file) | 14.45–15.95 |
| `Destruction/chunk_land_05.wav` | Mattia Cellotto - Rocks Momentum | (same file) | 17.65–19.2 |
| `Destruction/debris_small_01.wav` | Mechanical Wave - Rock Brick and Dirt | Smash Rock On Debris_RBD 04.wav | 0–1.3 |
| `Destruction/debris_small_02.wav` | Alexander Kopeikin - Rocks | soil and dirt debris heavy 13.wav | 0–2.35 |
| `Destruction/debris_small_03.wav` | BluezoneCorp - Demolisher - Robot | Bluezone_BC0290_demolisher_debris_rubble_texture_004.wav | 0–2.1 |
| `Destruction/debris_fall_01.wav` | Alexander Kopeikin - Rocks | rock tumble down debris long 06.wav | 0–4.8 |
| `Destruction/debris_fall_02.wav` | Alexander Kopeikin - Rocks | rocks stream down heavy 02.wav | 0–8.8 |
| `Destruction/debris_fall_03.wav` | Mechanical Wave - Rock,Brick and Dirt - Part 02 | Crumbling Rock Slide_RBDII 07.wav | 0–4.2 |
| `Destruction/debris_settle_dusty.wav` | Tatak Audio - Rocks and Debris | Tatak_ROCKS_Pebbles Dust Tumbling Rolling Debris.wav | 0.75–7 |
| `Destruction/collapse_medium_01.wav` | BluezoneCorp - Building Collapse | Bluezone_BC0275_building_collapse_debris_falling_rock_rubble_008.wav | 0–6.4 |
| `Destruction/collapse_medium_02.wav` | BluezoneCorp - Building Collapse | Bluezone_BC0275_building_collapse_debris_falling_rock_008.wav | 0.45–7.2 |
| `Destruction/collapse_medium_glass.wav` | BluezoneCorp - Building Collapse | Bluezone_BC0275_building_collapse_debris_falling_rock_rubble_glass_010.wav | 0–2.55 |
| `Destruction/collapse_large_01.wav` | Justsoundeffects - Stones and Debris | DESTRClpse_Massive Wall Collapsing_JSE_SD.wav | 0.45–12.35 |
| `Destruction/collapse_large_02.wav` | Justsoundeffects - Stones and Debris | (same file) | 27.05–36.4 |
| `Destruction/collapse_large_03.wav` | Justsoundeffects - Stones and Debris | (same file) | 48.95–60.9 |
| `Destruction/wood_break_01.wav` | DavidDumais - Explosion SFX Pack | WOODCrsh_Designed Wood Crash And Debris 13_DDUMAIS_NONE.wav | 0–1.5 |
| `Destruction/wood_break_02.wav` | InspectorJ - Wooden Fence Destruction | Destruction_Wooden_2.wav | 0–1.4 |
| `Destruction/wood_break_03.wav` | Double Trouble Audio - Tools and Wood | Small Wood Pile, Smash.wav | 0–1 |
| `Destruction/wood_snap_01.wav` | InMotionAudio - Wood | WOODBrk_Snap09_InMotionAudio_Wood.wav | 0–0.85 |
| `Destruction/wood_snap_02.wav` | Matt Script - You Me & Debris | wood_breaking_cracking_snapping_breaking_peeling_bones_break_snap_crack_13.wav | 0–1.33 |
| `Destruction/wood_debris_fall.wav` | Eneas Mentzel - Debris & Rubble | DESTRCrsh_scrap wood falling  interior perspective_Eneas Mentzel_Debris & Rubble_05.wav | 0–2.62 |
| `Destruction/glass_break_01.wav` | Soundopolis - Glass Smash Full | Glass_Break_Pane_702T_Fienup_005.wav | 0–2 |
| `Destruction/glass_break_02.wav` | Chris Skyes - Shards Broken Glass | Window,Small,Break,Medium Impact,Large Amount of Clinking Pieces.wav | 0–1.39 |
| `Destruction/glass_break_03.wav` | BluezoneCorp - Broken Glass | Bluezone_BC0274_glass_impact_break_002.wav | 0–1 |
| `Destruction/glass_break_04.wav` | Airborne Sound - Elements Glass | Glass,Plate Glass,Thick,Break,Topple,Schoeps.wav | 0–1.6 |
| `Destruction/metal_shear_tear.wav` | Secret Source - 003 Jaws Of Life | JOL_0194_MetalDestruction_15_DPA4061.wav | 0–9.6 |
| `Destruction/metal_shear_tear_short.wav` | Secret Source - 003 Jaws Of Life | (same file) | 0.8–3.6 |
| `Destruction/metal_impact_heavy_01.wav` | BlueZone - Heavy Metal Impact Sound Effects | Bluezone_BC0251_heavy_metal_impact_large_tank_01_03.wav | 0–4.3 |
| `Destruction/metal_impact_heavy_02.wav` | BlueZone - Heavy Metal Impact Sound Effects | Bluezone_BC0251_heavy_metal_impact_metal_plate_medium.wav | 0–2.3 |
| `Destruction/metal_impact_03.wav` | Sound Spark LLC - Metal Hits, Scrapes and Squeaks | Metal_Sheets_Sledgehammer_Hit_02.wav | 0–1.2 |
| `Destruction/metal_collapse.wav` | Alexander Kopeikin - Black Metal | dry rusty metal impact with collapse 08.wav | 0–3.4 |
| `Destruction/metal_groan_stress.wav` | Alexander Kopeikin - Black Metal | heavy sheet metal low groan 13.wav | 0–7 |
| `Destruction/rebar_drop.wav` | Airborne Sound - Elements Metal | Metal,Rebar,Drop,Concrete,Low Height,3,Medium Distant.wav | 0–1.6 |
| `Destruction/debris_whoosh.wav` | Cinematic Sound Design - Colossal Impacts | Woosh Debris.wav | 0–2 |
| `Player/footstep_gravel_01..06.wav` | PMSFX - STEPS Dirt & Gravel | PM_SDNG_Stereo_Walk_Seamless_Loop_1.wav | 0.68–1.12, 1.24–1.68, 2.42–2.86, 2.96–3.4, 3.62–4.06, 5.34–5.78 |
| `UI/ui_select.wav` | Rescopic Sound - User Interaction | UIClick_Select Middle 29_RSCPC_USIN.wav | 0–0.59 |
| `UI/ui_confirm.wav` | Rescopic Sound - User Interaction | UIAlert_Confirm Middle 12_RSCPC_USIN.wav | 0–1.04 |
| `UI/ui_panel_open.wav` | Rescopic Sound - User Interaction | UIMvmt_Window Open Thin 05_RSCPC_USIN.wav | 0–1.03 |
| `UI/ui_hover.wav` | Kpow Sounds - UI SOUNDPACKS | UI_SoundPack11_Scroll_v09.wav | 0.48–0.62 |
| `UI/ui_back.wav` | Kpow Sounds - UI SOUNDPACKS | UI_SoundPack11_Back_v1.wav | 0.5–0.7 |
| `UI/ui_error.wav` | Kpow Sounds - UI SOUNDPACKS | UI_SoundPack11_Error_v3.wav | 0.48–0.66 |
| `UI/ui_click_mechanical.wav` | Eiravaein Works - Start Select | StartSelect,UI,set34,mechanical,subtle,percussive,thick,bright,holdrelease.wav | 0–0.47 |
