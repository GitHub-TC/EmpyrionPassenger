# Empyrion Passenger
## Installation
1. Download der aktuellen ZIP datei von https://github.com/GitHub-TC/EmpyrionPassenger/releases
1. Upload der Datei im EWA (EmpyrionWebAccess) MOD oder händische installation mit dem ModLoader aus https://github.com/GitHub-TC/EmpyrionModHost

Demo: https://empyriononline.com/threads/mod-empyrionpassenger.44246/

### Wo für ist das?

Man fliegt mit einer Gruppe von Leuten in einem Raumschiff und muss sich ausloggen. 
Wenn die anderen weiterfliegen befindet man sich nach einem Login aber immer noch an der gleichen Position nur ist das
Raumschiff ganz wo anders. Mit diesem Mod kann kann sich als Passagier an das Schiff binden und wird automatisch bei einem
Login an dessen aktuelle Position teleportiert.

#### Wie steuert man den MOD?

Die Kommandos funktionieren NUR im Fraktionschat!

#### Hilfe

* /pass help : Zeigt die Kommandos der Mod an

#### Teleport

* /pass => Passagier einrichten wenn man auf Pilot des Schiffes ist
* /pass <Id> => Passagier einrichten zu der Stuktur mit der <Id> - diese muss in der gleichen Fraktion sein
* /pass help => Liste der Kommandos
* /pass back => Falls ein Teleport schiefgegenen sein sollte kann sich der Spieler hiermit zu der Position VOR dem Teleport zurück teleportieren lassen
* /pass delete <Id> => Löscht alle Passagierinformationen von zum Schiff <Id>
* /pass list <Id> => Listet alle Passagiere von <Id> auf
* /pass listall => Listet alle Passagierinformationen auf (nur ab Moderator erlaubt)
* /pass cleanup => Löscht alle Passagierinformationen die zu gelöschten Strukturen führen (nur ab Moderator erlaubt)

Beispiel:
- Als Pilot des CV 4004: /pass
- Als normaler Passagier des CV: /pass 4004

Hinweis: Man kann sich aber auch in einem angedockten Schiff befinden und nur mit dem /pass Befehl seinen Passagierstatus anmelden

### Konfiguration
Eine Konfiguration kann man in der Datei (wird beim ersten Start automatisch erstellt)

[Empyrion Directory]\Saves\Games\[SaveGameName]\Mods\EmpyrionPassenger\PassengersDB.json

vornehmen.

* HoldPlayerOnPositionAfterTeleport: Zeit in Sekunden die ein Spieler nach dem Teleport auf Position gehalten wird bis die Strukturen (hoffentlich) nachgeladen sind
* PreparePlayerForTeleport: Zeit in Sekunden die der Spieler sich auf den Teleport vorbereiten kann (Chat schließen, Finger auf die Jetpacktaste und die Leertaste legen... ;-) )
* AllowedStructures: Liste der erlaubten Strukturen für Passagierinformationen hierbei sind folgende Werte erlaubt
  - EntityType: CV, SV, HV 
* PassengersDestinations: Hier werden die gespeicherten Passagierinformationen hinterlegt

### Was kommt noch?
Zunächst erstmal und damit viel Spaß beim Mitreisen wünscht euch

ASTIC/TC

***

English-Version:

---

## Installation
1. Download the current ZIP file from https://github.com/GitHub-TC/EmpyrionPassenger/releases
1. Upload the file in the EWA (EmpyrionWebAccess) MOD or manual installation with the ModLoader from https://github.com/GitHub-TC/EmpyrionModHost
You can find a compiled DLL version in the EmpyrionTeleporter/bin directory if you do not want to check and compile the mod myself ;-)

Demo: https://empyriononline.com/threads/mod-empyrionpassenger.44246/

### What is this?

You fly with a group of people in a spaceship and have to log out.
If the others continue to fly, they are still in the same position after logging in, only that is
Spaceship where different. With this mod can bind as a passenger to the ship and is automatically at a
login to its current location.

#### What are all the commands?

All commands only work in faction chat!

#### Help

* /pass help: Displays the commands of the mod

#### Teleport

* /pass => Passenger set up when piloting the ship
* /pass <Id> => Passenger set up to the structure with the <Id> - this must be in the same faction
* /pass help => list of commands
* /pass back => If a teleport should be counterfeit, the player can be teleported back to the position BEFORE the teleport
* /pass delete <Id> => Deletes all passenger information from to the ship <Id>
* /pass list <Id> => Lists all passengers from <Id>
* /pass listall => Lists all passenger information (only allowed from moderator)
* /pass cleanup => Deletes all passenger information leading to deleted structures (only allowed from moderator)

Example:
- As a pilot of the CV 4004: /pass
- As a normal passenger of the CV: /pass 4004

Note: You can also be in a docked ship and register your passenger status only with the /pass command

### Configuration
A configuration can be found in the file (automatically created on first startup)

[Empyrion Directory]\Saves\Games\\[SaveGameName]\Mods\EmpyrionPassenger\PassengersDB.json

make.

* HoldPlayerOnPositionAfterTeleport: Time in seconds that a player is held in position after the teleport until the structures are (hopefully) reloaded
* PreparePlayerForTeleport: Time in seconds the player can prepare for the teleport (close the chat, touch the jetpack key and the space bar ... ;-))
* AllowedStructures: list of allowed structures for passenger information the following values ​​are allowed
  - EntityType: CV, SV, HV
* PassengersDestinations: The stored passenger information is stored here

### Is that it?
First of all, and have fun traveling with you

ASTIC/TC
