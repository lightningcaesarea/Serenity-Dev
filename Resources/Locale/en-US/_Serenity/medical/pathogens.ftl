# Pathogens
pathogen-staphylococcus = Staphylococcus
pathogen-streptococcus = Streptococcus
pathogen-pseudomonas = Pseudomonas

# Narrow-spectrum antibiotics
reagent-name-cloxacillin = cloxacillin
reagent-desc-cloxacillin = A narrow-spectrum antibiotic that cures Staphylococcus infections and does nothing for any other. It works a tier at a time while it is in the blood, and is harmful in large doses: at 16 units or more it poisons the patient.
reagent-name-penicillin = penicillin
reagent-desc-penicillin = A narrow-spectrum antibiotic that cures Streptococcus infections and does nothing for any other. It works a tier at a time while it is in the blood, and is harmful in large doses: at 16 units or more it poisons the patient.
reagent-name-ciprofloxacin = ciprofloxacin
reagent-desc-ciprofloxacin = A narrow-spectrum antibiotic that cures Pseudomonas infections and does nothing for any other. It works a tier at a time while it is in the blood, and is harmful in large doses: at 16 units or more it poisons the patient.

# Blood culture analyzer and antibiotic synthesizer
pathogen-machine-unpowered = The machine has no power.
pathogen-analyzer-no-blood = There is no blood in that to test.
pathogen-analyzer-done = The analyzer prints a report.
pathogen-report-found =
    BLOOD CULTURE
    Pathogen: {$pathogen}
    Treat with: {$drug}
pathogen-report-unknown =
    BLOOD CULTURE
    Infection present, but the strain could not be told apart. Use a broad-spectrum antibiotic.
pathogen-report-clear =
    BLOOD CULTURE
    No pathogen found.
pathogen-report-inconclusive =
    BLOOD CULTURE
    Inconclusive. The patient this blood came from could not be matched.
pathogen-synthesizer-bad-report = That report names no pathogen.
pathogen-synthesizer-programmed = The synthesizer is programmed for {$pathogen}.
pathogen-synthesizer-not-programmed = The synthesizer needs a blood culture report that names a pathogen.
pathogen-synthesizer-no-source = There is no {$reagent} in that to convert.
pathogen-synthesizer-done = The synthesizer converts {$amount}u into {$drug}.
