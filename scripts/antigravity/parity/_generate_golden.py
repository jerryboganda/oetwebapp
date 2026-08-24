"""One-off generator: writes golden/writing.json (50) + golden/speaking.json (30).
Deterministic slot variation over hand-authored clinical bases. Run from repo root:
  agent-gateway\\.venv\\Scripts\\python.exe scripts\\antigravity\\parity\\_generate_golden.py
"""
import json
import os

OUT = os.path.join(os.path.dirname(__file__), "golden")

BASES = [
    ("nursing", "discharge", "Community Nurse", "cellulitis of the left lower leg",
     ["oral antibiotics for seven days", "daily wound measurement and dressing change", "elevate the leg when resting", "return if spreading redness or fever"],
     ["flucloxacillin 500 mg", "paracetamol"]),
    ("medicine", "referral", "Dermatologist", "a suspicious changing mole on the upper back",
     ["mole enlarged over three months", "irregular border and two shades of brown", "no bleeding or itching reported", "family history of skin cancer in father"],
     ["emollient only", "sun-protection advice"]),
    ("nursing", "information", "School Nurse", "newly diagnosed type 1 diabetes in a ten-year-old",
     ["blood glucose checks four times daily", "insulin injections before breakfast and dinner", "hypo signs include shaking and sweating", "snack needed after sport"],
     ["insulin aspart", "glucose tablets"]),
    ("physiotherapy", "discharge", "General Practitioner", "low back pain following a lifting injury",
     ["core stability exercises twice daily", "gradual return to lifting over four weeks", "pain improved from seven to two out of ten", "advised to avoid prolonged sitting"],
     ["ibuprofen 400 mg", "heat pack advice"]),
    ("occupational therapy", "referral", "Social Services Coordinator", "post-stroke difficulty with kitchen tasks",
     ["left-sided weakness limits grip", "kettle tipper and jar opener recommended", "one fall in the kitchen last week", "lives alone in a two-storey house"],
     ["aspirin 75 mg", "atorvastatin 40 mg"]),
    ("pharmacy", "information", "Practice Nurse", "warfarin counselling after atrial fibrillation diagnosis",
     ["same time each evening dose", "avoid new over-the-counter anti-inflammatories", "report unusual bruising or nosebleeds", "green leafy vegetables in consistent amounts only"],
     ["warfarin 3 mg", "beta-blocker"]),
    ("radiography", "referral", "Orthopaedic Surgeon", "persistent wrist pain after a fall",
     ["tenderness over the anatomical snuffbox", "initial X-ray reported as normal", "wrist splint worn for two weeks", "repeat imaging requested"],
     ["codeine 30 mg", "paracetamol"]),
    ("nursing", "discharge", "District Nurse", "venous leg ulcer showing early granulation",
     ["four-layer compression bandaging weekly", "ankle-brachial pressure index 0.9", "moisture balance dressings applied", "weight-bearing encouraged with stockings"],
     ["paracetamol", "zinc supplement"]),
    ("medicine", "referral", "Gastroenterologist", "iron deficiency anaemia with occult blood loss",
     ["haemoglobin nine grams per decilitre", "positive faecal occult blood test", "two-week wait referral criteria met", "no weight loss or change in bowel habit"],
     ["ferrous sulfate 200 mg", "omeprazole 20 mg"]),
    ("podiatry", "information", "Diabetes Specialist Nurse", "annual diabetic foot screening results",
     ["loss of protective sensation both feet", "dorsalis pedis pulses present", "annual review recommended", "custom insoles provided"],
     ["metformin 1 g", "simvastatin"]),
    ("speech therapy", "referral", "Ear Nose Throat Consultant", "hoarseness persisting beyond six weeks",
     ["teacher with heavy voice use", "no difficulty swallowing reported", "vocal hygiene advice already given", "smoker of ten cigarettes daily"],
     ["none prescribed", "proton pump inhibitor trial"]),
    ("nursing", "advice", "Practice Manager", "needle stick injury protocol update",
     ["immediate washing for five minutes", "wound covered with waterproof dressing", "occupational health review same day", "hepatitis B status up to date"],
     ["none required", "tetanus booster considered"]),
    ("physiotherapy", "referral", "Rheumatologist", "inflammatory arthritis suspected in both hands",
     ["morning stiffness lasting one hour", "symmetrical swelling of the finger joints", "rheumatoid factor negative", "symptoms began twelve weeks ago"],
     ["naproxen 500 mg", "omeprazole"]),
    ("occupational therapy", "discharge", "Care Home Manager", "seating assessment completed",
     ["pressure-relieving cushion fitted", "chair height raised by five centimetres", "staff repositioning plan agreed", "review in three months"],
     ["analgesia as prescribed", "none added"]),
    ("medicine", "discharge", "Home Visit Nurse", "heart failure exacerbation stabilised",
     ["weight down two kilograms since admission", "daily weights recorded each morning", "fluid restriction of one and a half litres daily", "report breathlessness at night urgently"],
     ["furosemide 40 mg", "bisoprolol 5 mg"]),
    ("nursing", "referral", "Continence Advisor", "stress incontinence after prostate surgery",
     ["leakage on coughing and lifting", "pelvic floor exercises taught incorrectly", "pad use of two per day", "onset six weeks after operation"],
     ["none prescribed", "anticholinergic avoided"]),
    ("paramedicine", "information", "Falls Clinic Coordinator", "elderly patient found on the floor overnight",
     ["low body temperature treated at scene", "no fracture identified", "carpet burns to the hip noted", "medication review requested"],
     ["donepezil", "bendroflumethiazide"]),
    ("dietetics", "referral", "Paediatrician", "faltering growth in an eighteen-month-old",
     ["weight crossed two centile lines downward", "three milk feeds replacing meals", "iron studies within normal range", "food diary shows grazing pattern"],
     ["vitamin D drops", "multivitamin syrup"]),
    ("mental health", "discharge", "General Practitioner", "adjustment disorder following bereavement",
     ["sleep restored to six hours", "part-time return to work planned next month", "bereavement support group joined", "no current thoughts of self-harm"],
     ["mirtazapine 15 mg", "sleeping tablet stopped"]),
    ("nursing", "information", "Travel Clinic Nurse", "pre-travel vaccination schedule",
     ["combined hepatitis A and typhoid vaccine given", "yellow fever certificate valid ten days after vaccination", "malaria tablets start one week before travel", "mosquito bite precautions listed"],
     ["atovaquone-proguanil", "anti-diarrhoeal kit"]),
]

PATIENT_SLOTS = [
    ("Mrs AB", 67), ("Mr CD", 54), ("Ms EF", 38), ("Mr GH", 72), ("Mrs IJ", 45),
    ("Mr KL", 61), ("Ms MN", 29), ("Mr OP", 80), ("Mrs QR", 58), ("Mr ST", 66),
]
DAY_SLOT = [3, 5, 7, 10, 14]


def build_writing():
    items = []
    for i in range(50):
        base = BASES[i % len(BASES)]
        profession, ltype, recipient, condition, facts, meds = base
        initials, age = PATIENT_SLOTS[(i // len(BASES)) % len(PATIENT_SLOTS)]
        age += i % 5
        day = DAY_SLOT[i % len(DAY_SLOT)]
        items.append({
            "id": f"w{i + 1:03d}",
            "profession": profession,
            "letterType": ltype,
            "recipient": recipient,
            "patientInitials": initials,
            "patientAge": age,
            "dayOfAdmission": day,
            "taskNotes": (
                f"{initials}, aged {age}, admitted on day {day} with {condition}. "
                f"Key management: {facts[0]}; {facts[1]}; {facts[2]}. "
                f"Safety-netting: {facts[3]}. Current medications: {meds[0]}, {meds[1]}. "
                f"Write the {ltype} letter to the {recipient}."
            ),
            "expected": {
                "criterionCodes": ["purpose", "content", "conciseness", "genre", "organization", "language"],
                "maxScores": {"purpose": 3, "content": 7, "conciseness": 7, "genre": 7, "organization": 7, "language": 7},
                "mustCoverFacts": facts,
                "minTotalScore": 16,
            },
        })
    return items


SCENARIOS = [
    ("chest pain on climbing stairs", "high", ["mentions the exertion trigger", "describes when it started", "agrees to urgent checks"]),
    ("persistent dry cough for three weeks", "medium", ["states duration clearly", "denies fever", "agrees to examination"]),
    ("wrist pain after falling on ice", "medium", ["describes mechanism of injury", "reports swelling", "worried about a fracture"]),
    ("recurrent migraines with visual aura", "medium", ["describes the visual disturbance", "mentions increasing frequency", "reports missed workdays"]),
    ("sore throat with swollen glands", "low", ["localises the pain", "denies breathing difficulty", "accepts simple pain relief"]),
    ("blood noticed in the urine", "high", ["reports visible blood", "admits some discomfort", "agrees to urgent tests"]),
    ("dizziness on standing quickly", "medium", ["links dizziness to standing", "mentions a new medication", "reports one near-fall"]),
    ("child with fever and a rash", "high", ["anxious parent speaks first", "reports rash not fading under pressure", "confirms fluid intake"]),
    ("tingling in both feet", "medium", ["describes sock-like distribution", "mentions long-standing diabetes", "asks about nerve tests"]),
    ("difficulty sleeping after job loss", "low", ["links mood to redundancy", "denies hopelessness when asked", "open to sleep advice"]),
    ("knee locking and giving way", "medium", ["describes locking episodes", "mentions an old football injury", "requests specialist opinion"]),
    ("palpitations while resting", "high", ["describes pounding heartbeat", "reports episodes lasting minutes", "mentions high caffeine intake"]),
    ("bloating with alternating constipation", "low", ["describes a months-long pattern", "denies weight loss", "asks about diet changes"]),
    ("breathlessness lying flat", "high", ["now needs two pillows", "reports ankle swelling", "mentions a past heart problem"]),
    ("ear pain after a swimming holiday", "low", ["localises pain to one ear", "gives recent swimming history", "accepts explanation about drops"]),
    ("excessive thirst and urination", "medium", ["quantifies night-time trips", "mentions family diabetes", "asks whether it is diabetes"]),
    ("joint pain worse in the morning", "low", ["stiffness eases with movement", "small joints affected", "mother had similar symptoms"]),
    ("sudden blurred vision in one eye", "high", ["states sudden onset", "describes a curtain effect", "agrees to emergency referral"]),
    ("anxiety before public speaking", "low", ["normalises the fear at first", "reports physical symptoms", "asks for coping techniques"]),
    ("heel pain with the first step in the morning", "low", ["describes classic heel pattern", "mentions a new running routine", "accepts stretching advice"]),
    ("night sweats and fatigue", "medium", ["needs to change bedsheets", "reports low-grade temperature", "mentions slight weight loss"]),
    ("difficulty swallowing food", "medium", ["distinguishes pills from food sticking", "denies painful swallowing", "worried about blockage"]),
    ("evening confusion episodes", "high", ["family member describes evenings", "reports one wandering incident", "asks about a safety plan"]),
    ("itchy raised wheals after a new antibiotic", "medium", ["links rash to the new drug", "describes wheals moving", "already stopped the medication"]),
    ("loss of smell after a head cold", "low", ["notices food tastes bland", "cold resolved weeks ago", "asks whether it is permanent"]),
    ("leg cramps at night", "low", ["describes calf cramping", "relieved by stretching", "mentions a water tablet"]),
    ("weak voice by end of teaching day", "low", ["professional voice user", "no pain reported", "drinks more water on good days"]),
    ("bruising easily without injury", "medium", ["shows forearm bruises", "started a blood thinner recently", "denies nosebleeds"]),
    ("burning urination with frequency", "medium", ["describes stinging sensation", "states increased frequency", "denies back pain"]),
    ("tremor in the right hand at rest", "medium", ["noticed by family first", "worse when resting", "grandfather had a tremor"]),
]


def build_speaking():
    items = []
    for i, (topic, sev, elems) in enumerate(SCENARIOS):
        items.append({
            "id": f"s{i + 1:03d}",
            "scenarioTopic": topic,
            "candidatePrompt": (
                "The candidate is role-playing a healthcare professional. The patient has just "
                f"raised: '{topic}'. Produce the patient's next turn."
            ),
            "expected": {
                "severityExpected": sev,
                "roleplayElements": elems,
                "minChars": 40,
                "maxChars": 900,
            },
        })
    return items


def main():
    os.makedirs(OUT, exist_ok=True)
    writing = {"suite": "writing-examiner", "contract": "criteria-array-v1", "items": build_writing()}
    with open(os.path.join(OUT, "writing.json"), "w", encoding="utf-8") as f:
        json.dump(writing, f, indent=1)
    speaking = {"suite": "speaking-interlocutor", "contract": "patient-utterance-v1", "items": build_speaking()}
    with open(os.path.join(OUT, "speaking.json"), "w", encoding="utf-8") as f:
        json.dump(speaking, f, indent=1)
    print("writing items:", len(writing["items"]))
    print("speaking items:", len(speaking["items"]))


if __name__ == "__main__":
    main()
