surgery-cleanliness-0 = It is [color=#6cdcff]sterile[/color].
surgery-cleanliness-1 = It looks [color=#a6e8ff]clean[/color], with only a trace of use.
surgery-cleanliness-2 = It is [color=#dddd77]a little grimy[/color].
surgery-cleanliness-3 = It is [color=#e6b84f]noticeably dirty[/color].
surgery-cleanliness-4 = It is [color=#e67a3c]filthy[/color].
surgery-cleanliness-5 = It is [color=#d83030]caked in grime and blood[/color].
surgery-contaminated = It carries traces of [color=#d83030]other patients' blood[/color].

surgery-clean-verb = Sanitize
surgery-clean-verb-message = Wipe the dirt and traces of other patients off with soap.
surgery-cleaning = You start cleaning { THE($target) }.

surgery-unsterile-warning = Your dirty tools and gloves are making the patient sick!

reagent-name-exudate = exudate
reagent-desc-exudate = A pus-yellow liquid drained from infected tissue. It has no use, and smells like it.

reagent-name-antibiox = antibiox
reagent-desc-antibiox = A broad-spectrum antibiotic. Needs no diagnosis: it blocks new infections and freezes an existing one where it is, but it does not cure it, and the infection resumes when the drug wears off. Wipes out helpful gut bacteria, leaving the patient weak, and is harmful in large doses: at 16 units or more it poisons the patient, stops protecting them, makes new infections much likelier and makes infections spread faster.

# Admin command: infect
cmd-infect-desc = Give a mob a wound infection for testing, or move an existing one to the given tier. Ignores antibiotics.
cmd-infect-help = Usage: infect [entity uid] [tier 1-3]
    With no entity, targets your own mob. Tier defaults to 1.
cmd-infect-bad-entity = No such entity: {$entity}
cmd-infect-no-target = No target: attach to a mob or pass an entity uid.
cmd-infect-bad-tier = The tier must be a whole number from 1 to {$max}.
cmd-infect-no-wounds = {$entity} has no wound tracking, so it can't be infected.
cmd-infect-done = {$entity} now has a tier {$tier} infection.
cmd-infect-hint-entity = <entity uid>
cmd-infect-hint-tier = <tier 1-3>
