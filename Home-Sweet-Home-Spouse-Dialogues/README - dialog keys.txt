Home Sweet Home: Spouse Dialogues
=================================

This mod queues extra spouse dialogue without editing the game's marriage-dialogue assets.
That lets its scheduled dialogue coexist with dialogue added by other mods.

Dialogue data
-------------

Edit this file to add or change dialogue:
  i18n\pt.json

The supported keys are unchanged:
  springHomeDay.Sebastian.female.1
  springHomeNight.Sebastian.1
  springRainDay.Sebastian.1

The same structure works for vanilla and modded spouses. Seasonal keys are tried
first; HomeDay, HomeNight, and rainDay are fallbacks.

Item gifts
----------

Use [item:ObjectId], [item:ObjectId:Amount], or
[item:ObjectId:Amount:fullBagKey] in a dialogue line.

The custom drink IDs are:
  SiL.IcedWaterCup
  SiL.IcedWaterBottle
  SiL.IcedCoffeeCup
  SiL.CreamCoffeeCup
  SiL.HomemadeShake
  SiL.RedBerryShake
  SiL.BlackberryShake

Example:
  "Leva uma água gelada para a fazenda.[item:SiL.IcedWaterBottle]"
