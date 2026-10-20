# Old Colored Keycards

Brings the colored Labs keycards back to how they were before 0.15:

- **Infinite uses**, or pick your own number.
- **Old Flea prices** from the end of PvE 0.14.
- **Old spawns**: keycards spawn where they did in 0.14.
- **Old boss loot**: bosses and cultists carry them as often as in 0.14.
- **Old handbook prices**: traders pay 0.14 prices for keycards. The Flea keeps the old Flea prices and traders pay what they did in 0.14.

Everything can be turned off in the config.

| Keycard | Flea price |
|---|---|
| Blue | ₽6,100,000 |
| Red | ₽11,300,000 |
| Green | ₽11,500,000 |
| Black | ₽1,650,000 |
| Violet | ₽4,000,000 |
| Yellow | ₽2,300,000 |

## Install
Extract the mod's zip into your SPT folder. Server only

## Settings
`SPT_Runtime\user\mods\OldColoredKeycards\config.jsonc`. Each setting has a note. Restart the server after changing it. Delete the file to get the defaults back.

## Coming from 3.11
This is a C# rewrite for SPT 4.1, so the old `config.json` won't work. The 3.11 version is at https://github.com/MrFums/OldColoredKeycards.

## Where the 0.14 spawns come from
SPT 3.9.8's database (EFT 0.14.9). `Tools/Calibrate` turns it into `Keycards/Data/old-keycard-spawns.json`. Thanks to the SPT team.
