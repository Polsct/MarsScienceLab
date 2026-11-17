# Mars Science Lab

Mars Science Laboratory (2011–2012) for KSP 1.12: cruise stage, aeroshell, Sky Crane and the Curiosity rover, built
at true size from public sources. Entry, descent and landing fly themselves.

**Version:** 1.0.0 · **Author:** Polsct · **License:** MIT, see [LICENSE.md](MarsScienceLab/LICENSE.md)

## Requirements

- KSP 1.12.x
- ModuleManager 4.2+
- Textures Unlimited (without it the parts render black)

The mod warns at start if something is missing or the folder is in the wrong place.

**Optional:** RealSolarSystem / RealismOverhaul, RP-1, RealFuels, RealAntennas, Kerbalism, FAR, RealChute, Waterfall,
MechJeb2, Contract Configurator, Firefly. Their patches apply only when the mod is installed. TweakScale is removed
from the MSL parts (see [Scale](#scale)).

## Installation

1. Delete any older `GameData/MarsScienceLab` (and old `MSL_Aeroshell`, `MSL_CruiseStage`, `MSL_SkyCrane`,
   `MSL_Curiosity` folders).
2. Copy `MarsScienceLab` into `KSP/GameData`.
3. Copy the craft files from `Ships/VAB` into `KSP/Ships/VAB`.

| Craft | Contents |
|---|---|
| `MSL_Guided_Entry` | Full spacecraft, 1:1 (RSS) |
| `MSL_Guided_Entry_Stock` | The same at 0.625x (stock) |
| `MSL_Curiosity_2012` | Rover alone, 1:1 |
| `MSL_Curiosity_2012_Stock` | Rover alone, 0.625x |

## Flying a mission

1. Launch and fly the transfer to Mars (Duna) yourself. For course corrections, each REA cluster has a forward
   and a reverse engine: start it from the part menu or an action group and burn on the throttle.
2. Separate the cruise stage before entry and turn on entry guidance (MSL Status toolbar window, or the action
   **Enable MSL entry guidance**). Entry guidance targets Bradbury Landing by default.
3. Auto EDL (on by default) deploys the parachute, drops the heat shield, separates the backshell, flies the powered
   descent, lowers the rover on the bridle and flies the descent stage away. Any step can be triggered by hand; turn
   Auto EDL off to fly it all manually. The mod never switches vessels, changes time warp or saves.

## Rover and science

After landing, press **Deploy mobility** and drive with the normal rover controls. Drive speed, arm, mast and
**Drill sample** are in the rover part menu. Ten experiments:

| Experiment | Requirement |
|---|---|
| SAM, CheMin | Drilled sample from the current biome |
| MAHLI, APXS | Arm turret placed on the surface |
| ChemCam, Mastcam | Mast raised |
| DAN, RAD, REMS | Landed |
| MARDI | During descent and on the surface |

## Scale

With RealSolarSystem the parts are 1:1. Otherwise size follows the home world radius (0.625x stock, 0.76x JNSQ,
0.83x KSRSS), with mass, thrust and areas scaled to keep real-vehicle performance. Set a fixed size in
`MSL_Scale.cfg` (`scale` = 0.2 to 2). Scaled craft files are written to `KSP/Ships/VAB` at start.

## Contracts

With Contract Configurator in career, the stock agency Probodobodyne Inc offers two contracts: **Mars Science Laboratory** (land Curiosity
anywhere on Mars or Duna) and then **Curiosity: first drill** (transmit ChemCam, MAHLI, APXS, CheMin and SAM data
from the surface).

## Notes

A fan recreation of NASA's Mars Science Laboratory from public reports and photographs; some dimensions are
estimated. Not affiliated with or endorsed by NASA, JPL or the spacecraft's builders. Parts of the rover mesh are
derived from the Curiosity Rover model by NASA 3D Resources (NASA/JPL-Caltech); the rocker-bogie suspension
meshes are derived from the Mars 2020 Perseverance Rover model by NASA 3D Resources (NASA/JPL-Caltech).
