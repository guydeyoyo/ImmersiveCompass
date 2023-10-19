# Immersive Compass
A Skyrim-style compass for Valheim designed to improve immersion in the game world, based on aedenthorn's original with fixes and QoL additions.

Adds a customizable compass to the game HUD that:
* Can rotate with the camera or player model.
* Shows nearby map marker pins.
* Can show (or hide) player pins.
* Allows you to hide specific pin names and types.
* Allows you to drop a pin at your current location.
* Allows you to auto-remove your own tombstone pins on interaction.
* ServerSync for Pins (Distance), Show Player Pins, Ignore Pin Names, and Ignore Pin Types.

As with aedenthorn's original - you can change the scale of the compass and pins, distance of pins shown, and ignore specific pin names and types. You can also add an overlay and underlay image if required.

So what's changed? I've tried to fix a few game breaking bugs and add QoL pin related improvements - such as auto-removal of tombstone markers and server sync for minimum and maximum distances of pins.


## Default Controls
* **Add a Pin to Current Location:** (NumPad) *
* **Remove Nearest Pin:** LeftControl + (NumPad) *


## Configuration
A configuration file ``BepInEx\config\Yoyo.ImmersiveCompass.cfg`` is created after starting the game. You can adjust the values in this configuration file using a standard text editor or *Config Editor* in r2modman. Most of the configuration can be changed on-the-fly, though images and keybinds need a restart.


## Server Synchronization
The following configuration options are synchronized with the server, if enabled server-side.
* Mod Enabled/Disabled
* ServerSync Enabled/Disabled
* Pins - Minimum and Maximum Distance
* Show Player Pins
* Ignore Pin Names
* Ignore Pin Types


## Installation
Unzip the contents to your ``BepInEx\plugins`` folder.


## Thanks

* This is a fork of [GuyDeYoYo's ](https://github.com/guydeyoyo)'s [ImmersiveCompass](https://github.com/guydeyoyo/ImmersiveCompass), updated to work with Valheim 0.217.25 thanks to KG. 

## License

MIT No Attribution


## Changelog

### 1.2.0 
* Updated to work with Valheim 0.217.25 by updating source code libs and building off latest serversync (according to the mod-commissionee) 

### 1.1.2

* Updated references to work with game version 211.11.
* Updated ServerSync to 1.13.


### 1.1.1

* Fix to allow you to open your tombstone if auto-remove pin is enabled and pin does not exist.


### 1.1.0
If upgrading from 1.0.1, you need to delete your existing config file and set up a new config file.

* Added optional center marker image (not enabled by default).
* Added ServerSync for: Mod Enabled/Disabled, Pins (Minimum and Maximum Distance), Show Player Pins, Ignore Pin Names, and Ignore Pin Types.
* Changed keybinds from strings to keycodes in configuration.
* Changed default keybind from P to * (Times/Asterisk on NumPad).
* Changed keybind checking to prevent self-clash situations.


### 1.0.0
Initial release.
