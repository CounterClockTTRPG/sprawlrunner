The interface fonts, shipped with the game (they replace the Windows system
fonts it used to ask for, which other systems lack and which may not be
redistributed). All are under the SIL Open Font License 1.1; each family's
licence is beside it. Taken unmodified from https://github.com/google/fonts (ofl/).

  Barlow-Regular.ttf             body text            Barlow-OFL.txt (The Barlow Project Authors)
  BarlowCondensed-SemiBold.ttf   headings             Barlow-OFL.txt
  Inconsolata.ttf                the terminal         Inconsolata-OFL.txt (The Inconsolata Project Authors; renamed from Inconsolata[wdth,wght].ttf)
  StardosStencil-Bold.ttf        rubber stamps        StardosStencil-OFL.txt (Vernon Adams)
  ShareTechMono-Regular.ttf      receipts, tags, IDs  ShareTechMono-OFL.txt (Carrois Type Design)

UITheme loads them through Assets.Font; a missing file falls back to the old
system font, so the game never breaks without them.
