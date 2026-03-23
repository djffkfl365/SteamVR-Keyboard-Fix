# SteamVR Keyboard Fix
## Summary
This program removes en-US keyboard layout added by SteamVR(or related process) on windows unintentionally.

## Mechanism


## TODO
- Verify and properly implement RemoveRegisteredLayout method.
  - Assuming this method isn't related with purpose of this program.
  - SteamVR loads en-US to HKL but doesn't write any registery.
- Internalize installation process(Currently power shell script)
- Write readme.md properly, ideally with Japanese, Korean, English.
- Change code annotations to english.

## Note
- This program is written with LLMs, mainly Claude.
- This program is verified only on Windows.
  - Windows 10 Education 22H2 Build 19045.6456
- I'm not planning to support other OS such as Linux, Mac, etc.

