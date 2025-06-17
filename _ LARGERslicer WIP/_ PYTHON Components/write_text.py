__author__ = "Moritz Wesseler"
__version__ = "2024.10.23"


import os

file =Folder + name + ext


if write:
    filePath = open(file, "w") # Open the file
    for line in lines: # Iterate through lines
        filePath.write(line + "\n") # Write separate lines
    filePath.close() # Close the file
__author__ = "Moritz Wesseler"
__version__ = "2024.10.23"

import os

# Angenommen, Folder, name, ext und lines kommen von deinen Grasshopper-Inputs
file_path = os.path.join(Folder, name + ext)

if write:
    # Datei mit Kontextmanager öffnen – schließt automatisch
    # encoding angeben, um Unicode-Probleme zu vermeiden
    with open(file_path, "w", encoding="utf-8") as f:
        for line in lines:
            # f-String für saubere Zeilenumbrüche
            f.write(f"{line}\n")