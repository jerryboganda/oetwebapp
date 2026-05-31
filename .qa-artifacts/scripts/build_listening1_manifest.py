import json

# Transcripts extracted from Audio-Script PDF, split per extract at ---***--- markers
partA_ex1_transcript = """M: So, Mrs Dove. I have your notes here, but it would be good to hear things from your perspective: how all this started, what treatments you've had - anything you think I should be aware of.
F: Well, ... where to begin? I should explain that I'd been suffering with endometriosis for quite a long time, ever since my son was born actually and he's at university now. So, I was used to a certain level of discomfort - you know feeling bloated a lot of the time, getting the occasional bouts of nausea and feeling quite a bit of fatigue. Anyway, after a while, I also started to get pain in my lower back, and that gradually got worse and worse. I was working as a pre-school teacher in those days and had a young family of my own - it was a tough time. In the end I had to give up my job. Anyway, to cut a long story short, I was eventually diagnosed with cervical cancer. I was fairly lucky, I guess, it hadn't advanced too much and I was advised to have a hysterectomy. Although I was out of action for a while, it all went relatively smoothly. And, of course, it also sorted out my other problems - so for a while I was relatively well. My kids were at school by this time, so I was able to start my own business, working online as a recruitment consultant. I'd put both illnesses behind me, and I was feeling very positive.
M: But then you started to develop other symptoms?
F: That's right. Those feelings of fatigue started coming back. I'd find I was exhausted after a full day of online meetings. If I did have to go anywhere and do anything, like attend the occasional conference - especially if involved in driving any distance - that would just wipe me out. So, something obviously wasn't right. Anyway, I went to see the doctor and he said that it sounded like I might have iron-deficiency anaemia, and he did some blood tests. But when the results came back, I was really surprised to find I actually had iron overload and the next step was to test for haemochromatosis. And, of course, that came back positive. The first thing the doctor asked was whether anyone else in the family had ever had it because it can be genetic. Anyway, I didn't know for sure because I was adopted as a baby and I'd only recently traced my birth mother and started to get to know her. Anyway, she was able to confirm that some cousins whom I'd never met had indeed got the same thing.
M: I see. So, have you been having treatment?
F: Yes, I was told that the only option was venesection. That came as a bit of a shock and I had it every week initially, then once a month and I've been going every three months for some years now. I mean, it's fine. It hurt a bit at first, but I'm left with quite a bit of scarring on the arm where the needle goes in - but it's something I can live with. For the last few years, I've been back to my usual energetic self.
M: So, what brings you here today?
F: Well, I've started to develop some other problems - and I'd just like to have them checked out. I mean, I'm not getting any younger, so these could be completely unconnected with the blood problem. Anyway, the first thing is that I've started to get a bit of stiffness in my joints - it's particularly noticeable in my fingers, but I think it's there in my knees too. Then, I also seem to be getting more thirsty than usual - and it doesn't seem to be related to how much I drink. And the other thing is that I get a bit short of breath sometimes, even when I haven't been doing anything particularly strenuous - you know just walking the dog or washing the car and it suddenly comes on. None of these things is a big deal, but I've just noticed the change.
M: Sure. I'll ask you a bit more about those symptoms in a moment, but what I'd like... [fade]"""

partA_ex2_transcript = """F: So, Marvin, I understand you've been referred to me because you're experiencing some symptoms of long Covid - and your doctor's suggested that some physiotherapy could help.
M: That's right.
F: I've got your notes here, but as we haven't met before, could you just run through for me, how all this started, any treatment you've had and anything else you feel I should be aware of.
M: Yeah sure. It all started when I caught Covid-19. I mean, I hadn't been vaccinated, so maybe it was worse than it might've been - but who knows. Anyway, I had it pretty bad, but not bad enough to go to the hospital. I had the usual flu-like symptoms that turned into a dry cough. It completely wiped me out - I never felt so sick in my whole life, but I got over it and in time the symptoms pretty much disappeared. I was coughing for about a month, but otherwise I thought I'd beaten it.
F: I see. So, when did the Long Covid symptoms start?
M: I'd say..., like six weeks afterwards - the cough had gone, but I still wasn't feeling a hundred percent. Looking back, the whole experience had, kind of, sapped my energy. I didn't feel like going to the gym like I used to do before because I was getting out of breath just doing ordinary things - like carrying groceries. Then other stuff started happening. I wasn't sleeping properly and so I was tired pretty much the whole time. Like, I'd wake up with a headache and it would go on all day - just get worse and worse, and that's when the other symptoms would kick in. Like, my brain would go kind of fuzzy - so I couldn't focus on anything properly, and I'd start to get this thing where I couldn't remember stuff - like words would be on the tip of my tongue - but just wouldn't come out. I mean, that was weird. Anyway, at first, I didn't make the connection with Covid, I assumed something else was going on - and when I started to get palpitations and chest pains, I went to see the doctor - because I thought it must be a problem with my heart.
F: Yes, of course. So, did you get a diagnosis and treatment then?
M: Well, I had all sorts of tests, including an ECG and they put me on a heart monitor for five days. And that's when they said it was arrythmia and they put me on beta blockers. And I did start to feel better - like almost immediately. And they also said that my vitamin D levels were low and that I should take supplements for that - and that helped too. And they said this was all down to long-Covid apparently, and I'm still getting some strange symptoms despite the medications - and that's why I've come to see you.
F: OK - tell me about those, and how I might be able to help you.
M: Well, basically if I overdo things - like if I try to do too much or if I get anxious, then I start getting the symptoms again - mostly fatigue, but also a certain amount of joint pain - often in my hips, but it can also affect my ankles - even my feet sometimes. It's like a dull ache - and it seems to be related to the fatigue. I also get a certain amount of numbness in my fingers, which is a bit worrying. I mean I still do quite a bit of exercise, but I mostly do gentle stuff - like I've always done yoga - but I've also recently started doing tai chi - and that seems right for me. I would like to start going to the gym again, though I'm aware that I shouldn't do anything too strenuous. So, I was hoping to get some guidance on strength-building exercises. I mean, should I be using things like resistance bands or even trying light weights? It would be really good to do things like that, but I feel I need to do it under someone's guidance at the moment.
F: Yes, of course. well, thank you for all that background, Marvin - that's really given me a good idea of what we need to do. I'd like to start by ... [fade]"""

partB_25 = """F: Hi there. I'm Dorrie. I'll be your nurse while you here at the rehabilitation centre. How are you feeling today?
M: Okay, just a bit tired after the surgery.
F: That's understandable. I need to go over the falls policy with you. As you know, falls can be a significant risk for patients, especially after hip-replacement surgery like yours. It's important to take precautions to minimise the risk of injury. Are you familiar with the policy?
M: Well, I know to use the call bell if I need help getting out of bed and to use my frame when I'm walking. I was told to keep my personal belongings within reach and report any spills immediately. What worries me is that I keep coming over dizzy when I go to stand up. I guess I should just keep still and wait till it passes.
F: You certainly shouldn't try to move - and press your call button if you can and we'll be straight with you."""

partB_26 = """F: My main concern is Pavel. He's an 85-year-old male who has a diagnosis of vascular dementia. He lives at home with his wife, Maria. She has her own health problems, but generally seems to manage well, and there's a daughter living locally.
M: OK.
F: Pavel had a significant ischaemic stroke five years ago and since then has presented with memory problems and occasional non-distressing hallucinations. He has some expressive dysphasia and dribbling of saliva for which a speech pathologist offered conservative advice. He also has COPD that he self-manages with inhalers. He was diagnosed with a hydrocele, which presented shortly after his dementia diagnosis and causes him a bit of distress. He keeps forgetting it's there, so you may need to go over with him how to avoid discomfort when sitting down or using the bathroom.
M: OK."""

partB_27 = """M: Today, we're talking about how, as nurses, we can support the best resuscitation outcomes in our patients. Now, unlike adult cardio-respiratory arrest, that's mostly caused by ventricular fibrillation, most paediatric cardio-respiratory arrests are secondary arrests caused by hypoxia, as a direct result of underlying illness or injury. So, when a child's condition is deteriorating, it's vital to provide airway, breathing and cardiac support to prevent progression to cardio-respiratory arrest. Having a thorough understanding and knowledge of the equipment on the paediatric emergency trolley means you can select the equipment you need to manage the deteriorating patient in an emergency situation. So, that's what we're focussing on in this session. The equipment is stored in an emergency trolley which has specific equipment for either airways, breathing, circulation or disability stored in each of its four drawers."""

partB_28 = """F: OK, so the first patient today is Mrs Olvera - she's 86 years old and recently widowed. She's finding living alone rather challenging and her function has declined, leaving her with increased anxiety.
M: I see.
F: Her son's staying temporarily, but he's due to go home next week. So, what we're doing today is trying to increase her confidence when preparing their lunch. We'll get her to gather all the items she needs and set the table and then supervise her heating the meal - giving as much reassurance as we can.
M: I see.
F: If that goes well, then we can move on to the stairlift - because she needs to access upstairs and she's particularly anxious about the controls. This is where the son's rather inclined to take over. That's understandable, but we need him to see that it's better to guide and support her rather than trying to do things for her. But we may not get to that today.
M: Sure."""

partB_29 = """F: So, how are you getting on with the omeprazole you're taking for your acid reflux?
M: Well, there's no doubt it does the job - I've hardly been woken up by the reflux at all - unless I forget to take it of course. It's meant to be an hour before dinner - I don't always manage to stick to that.
F: Well, to get the full benefit you do need to take it as directed.
M: I realise that. The thing that's bothering me actually is that I've read how it can lead to other problems eventually if you're on it too long - like bone fractures, kidney disease, even infections. I'm not keen on that idea.
F: Well, the benefits definitely outweigh any risks - as long as you don't exceed the recommended dose.
M: I think I'm more inclined to try and wean myself off it if I can.
F: I wouldn't advise that actually. We'll monitor you and adjust it as needed."""

partB_30 = """M: So, how can I help you?
F: My big toe's very sore - especially if I walk any distance or put my weight on it. I've been taking painkillers and using an ice-pack, but it's not getting any better.
M: I see. Have you ever had any problems like this before?
F: Well, yes. I had gout a few years ago - but not in this toe. My doctor at the time suggested losing weight and adjusting my diet - which I did and it cleared up without too much trouble.
M: Perhaps you've knocked it somehow?
F: Well, I did trip over a kerb last week - it hurt a bit at the time, then I forgot about it. But a couple of days later this started. Do you think that it's set the gout off again somehow?
M: It's possible. Is it OK if I examine your toe?
F: Yes, of course."""

partC_ex1_transcript = """My name's Pietro Everall and my presentation is about cholesterol - and it's a fascinating, if rather complex, topic.
And that probably accounts for why, amongst our patients, there's a lot of misunderstanding about the role of cholesterol and about when and why it represents a health issue. To my mind, this largely results from the rather loose use of the word, particularly by journalists and others, that leads to cholesterol being perceived as a bad thing in the patient's mind. As doctors and nurses, we have to find a way of telling our patients exactly what cholesterol is, in simple terms, and why it's important - and that means going back to the basic science. It's long been established that cholesterol, by helping to move fat around the body, taking it to the organs that need it, is essential for health; that without it the body wouldn't be able to function. But when it becomes oxidised or damaged, cholesterol can contribute to the build-up of plaque in the arteries, increasing the risk of heart disease and stroke.
So, we often talk to patients about 'good' and 'bad' cholesterol - as a way of avoiding more technical definitions - because there are different types of cholesterol particles. For example, low-density lipoproteins - often called LDL particles are more likely to become oxidised and contribute to build-up of plaque - whereas larger, high-density or HDL particles are less likely to. So, the labels 'good' and 'bad', although simplistic, can help patients to see that we need to look at the whole picture - rather than just focussing on the total amount of cholesterol in the body - and also that advanced lipid testing is important - because it gives us information about the different types of cholesterol particles, and helps us identify patients needing treatment.
But patients can be reluctant to engage with the issue of cholesterol. As we know, when it comes to treatment, early intervention is key - but patients don't always see this. One thing they find hard to grasp is that although they feel perfectly fit and healthy - high cholesterol can be building up inside blood-vessel walls, narrowing them and reducing blood flow to the heart and brain - thereby increasing the risk of cardio-vascular problems. That's why it's imperative for those in high-risk groups - essentially men over 45 and women over 55 - to have regular blood-tests, to measure not just the total amount of cholesterol in the blood, but also levels of HDL, LDL and triglycerides - a fatty substance similar to bad cholesterol.
And preventive medicine has a key role to play here. It's estimated that 60% of adults in high-risk age groups have raised cholesterol levels, and whilst genetic factors are sometimes in play, in most cases it's just the result of poor diet, obesity and lack of exercise - often reflecting the habits of a lifetime. So, it's clear that we should be talking to younger patients about these issues too - and not just about diet and exercise either. There's research to suggest a link between stressful situations and how the body metabolises fat - and that's in addition to the fact that stressed-out people are more likely to smoke or have poor diets. So, we should be underlining the need for a good work-life balance; for taking regular breaks and managing stress levels in the workplace - long before patients enter the high-risk demographic.
Traditionally, statins have been the most commonly prescribed medication for high cholesterol - and that generally means a daily dose, taken orally, for life. Statins lower LDL levels by slowing down the production of cholesterol in the liver. But a new drug called Inclisiran works in a different way - targeting a gene that produces the protein PCSK9 to encourage the liver to absorb more 'bad' cholesterol from the blood and break it down. As well as only requiring twice-yearly injections, making it much more convenient, the drug has fewer side-effects, which with statins can include headaches and digestive problems, and studies show that treatment can reduce cholesterol by up to 50% in as little as two weeks.
The drug's an example of what's called 'gene silencing.' This is a unique mechanism that aims to disrupt the delivery of messages sent out by a gene that can cause illness - in the case of cholesterol, of the protein PCSK9. It doesn't touch the gene itself - an idea about which people do get nervous - and it bears no relation to things like gene editing, which also gets a bad press. The first such medication was a drug called Partisan, that was licensed in 2019 to treat amyloidosis - and work is continuing on similar drugs that could treat things like Huntington's Disease and pre-eclampsia. So, in rolling out these injections to control cholesterol, we could be looking at the future of how all disease will be treated."""

partC_ex2_transcript = """M: Today I'm talking to Lianne Haydock, a nurse with a special interest in the care of obese or 'plus-size' patients. This is a real issue, isn't it Lianne?
F: It is. The number of people carrying extra weight's been on the increase for some time. Obesity now affects around one in four adults in many western countries, and it's known to be associated with increased risk of health conditions including type-2 diabetes, heart disease and stroke. But we shouldn't forget that the reasons for this increase are complex and often very challenging to understand. So, addressing the root causes of the issue isn't the job of health professionals on the front line - our job is to offer optimal care for these patients. And this means moving away from treating them as the exception to the rule - towards making them an integral part of what we do. I'm committed to that idea, but making it happen is easier said than done - it means a major shift in both attitudes and policies.
M: So, what sort of attitudes towards plus-size patients have you come across?
F: One issue they face is the very common misconception that they're somehow responsible for their size - that it's simply a lifestyle choice. Even amongst health professionals you'll get comments like: 'If they'd just lose weight, there wouldn't be an issue.' This is so unfair, because the reasons for obesity are enormously varied and complex and I find comments like this very disheartening. I can understand that it's human nature to judge other people; to wonder why someone's become so overweight; to feel that it was somehow avoidable - but none of that justifies offering this patient group anything but the best possible standards of care - and that's what we should be aiming for.
M: But treating them must involve adopting a different approach sometimes?
F: Yes, but we need to focus on finding the best flexible approach that works for all patients - get away from the idea that the plus-size patient is unsuitable for the set-up we have in place. For example, it's the bed that's not suitable - rather than the patient who's too big to fit the bed. The notion of an average-size patient lying on a bed that isn't large enough or being expected to use a commode that's too small for them would be rightly regarded as inappropriate and undignified care. Ensuring that plus-size patients have appropriate equipment, and that everyone knows how to use it, is crucial.
M: And specialised equipment is available?
F: It's not that the equipment doesn't exist - there are specialist versions of most things from hoists to mobility aids to commodes, the problem is getting hold of these things in many hospitals. That's partly because, traditionally, it hasn't been a priority for funding and policies haven't kept pace with changes in society. But there are other issues here too. What really causes headaches is where you can order, say, a large bed, but the doors, corridors and stairs aren't wide enough to get it to you. Many older hospitals weren't designed with the needs of plus-size people in mind - so, even if you get the large bed in place, is there enough space for patients and staff to move around easily and so on.
M: But clinicians have other concerns - especially around safety issues, don't they?
F: Well, yes. For nurses in particular, one major concern involves moving and manual handling. If a patient's carrying a lot of excess weight, then more staff may be needed and there's a greater risk of somebody getting hurt. Turning a patient in bed, transferring to a chair, supporting a limb - all become much more complex, and sometimes daunting if the patient is plus-size. Clearly what's needed is a risk assessment, and information sharing is central to that - being able to map out the patient journey and anticipate needs helps us provide the best care. So, for a routine admission, nurses should know how the patient's going to arrive, where they're going to sit etc. - and not be suddenly confronted by someone whose needs can't be met.
M: And what about the human dimension?
F: Well, a key part of optimising care for these patients is avoiding assumptions about their capabilities. Nurses should have conversations with patients - ask how they normally handle specific tasks at home and see if this can be adapted into the healthcare setting. As one patient put it: 'We know we're large. We might be living under some delusion about the extent of it, or the damage that it may do to our long-term health, but we're not unaware.' So, although the subject needs to be handled with sensitivity, tact and dignity, it's relevant to care that it happens. And another important thing to remember is that the patient may feel worried on your behalf and be keen to work with you to reduce any risk."""

# ---- Part A questions ----
partA_ex1_questions = [
    {"number":1,"type":"gap_fill","noteTextBeforeGap":"discomfort from episodes of bloating,","correctAnswer":"(bouts of) nausea","acceptedAnswers":["bouts of nausea","nausea"],"points":1},
    {"number":2,"type":"gap_fill","noteTextBeforeGap":"developed","correctAnswer":"lower back","acceptedAnswers":["lower back"],"points":1},
    {"number":3,"type":"gap_fill","noteTextBeforeGap":"worsening condition affected her work as a","correctAnswer":"pre(-)school teacher","acceptedAnswers":["pre-school teacher","preschool teacher","pre school teacher"],"points":1},
    {"number":4,"type":"gap_fill","noteTextBeforeGap":"diagnosis of","correctAnswer":"cervical cancer","acceptedAnswers":["cervical cancer"],"points":1},
    {"number":5,"type":"gap_fill","noteTextBeforeGap":"underwent","correctAnswer":"(a) hysterectomy","acceptedAnswers":["a hysterectomy","hysterectomy"],"points":1},
    {"number":6,"type":"gap_fill","noteTextBeforeGap":"set up business as a","correctAnswer":"recruitment consultant","acceptedAnswers":["recruitment consultant"],"points":1},
    {"number":7,"type":"gap_fill","noteTextBeforeGap":"particularly noticeable after","correctAnswer":"driving (any distance)","acceptedAnswers":["driving any distance","driving"],"points":1},
    {"number":8,"type":"gap_fill","noteTextBeforeGap":"initially suspected","correctAnswer":"iron(-)deficiency anaemia","acceptedAnswers":["iron-deficiency anaemia","iron deficiency anaemia","iron-deficiency anemia","iron deficiency anemia"],"points":1},
    {"number":9,"type":"gap_fill","noteTextBeforeGap":"n.b.","correctAnswer":"adopted","acceptedAnswers":["adopted"],"points":1},
    {"number":10,"type":"gap_fill","noteTextBeforeGap":"has some","correctAnswer":"scarring (on the arm)","acceptedAnswers":["scarring on the arm","scarring"],"points":1},
    {"number":11,"type":"gap_fill","noteTextBeforeGap":"now experiencing stiffness in joints - in both fingers and","correctAnswer":"knees","acceptedAnswers":["knees"],"points":1},
    {"number":12,"type":"gap_fill","noteTextBeforeGap":"tendency to become excessively","correctAnswer":"thirsty","acceptedAnswers":["thirsty"],"points":1},
]

partA_ex2_questions = [
    {"number":13,"type":"gap_fill","noteTextBeforeGap":"contracted Covid-19 - wasn't","correctAnswer":"vaccinated","acceptedAnswers":["vaccinated"],"points":1},
    {"number":14,"type":"gap_fill","noteTextBeforeGap":"ongoing lack of","correctAnswer":"energy","acceptedAnswers":["energy"],"points":1},
    {"number":15,"type":"gap_fill","noteTextBeforeGap":"on waking - persisted all day","correctAnswer":"headache","acceptedAnswers":["headache"],"points":1},
    {"number":16,"type":"gap_fill","noteTextBeforeGap":"brain described as","correctAnswer":"fuzzy","acceptedAnswers":["fuzzy"],"points":1},
    {"number":17,"type":"gap_fill","noteTextBeforeGap":"tendency to forget things, e.g.","correctAnswer":"words","acceptedAnswers":["words"],"points":1},
    {"number":18,"type":"gap_fill","noteTextBeforeGap":"accompanied by chest pain","correctAnswer":"palpitations","acceptedAnswers":["palpitations"],"points":1},
    {"number":19,"type":"gap_fill","noteTextBeforeGap":"diagnosis of","correctAnswer":"arrhythmia","acceptedAnswers":["arrhythmia","arrythmia"],"points":1},
    {"number":20,"type":"gap_fill","noteTextBeforeGap":"low levels of","correctAnswer":"vitamin D","acceptedAnswers":["vitamin D","vitamin d"],"points":1},
    {"number":21,"type":"gap_fill","noteTextBeforeGap":"accompanied by joint pain: affects","correctAnswer":"hips","acceptedAnswers":["hips"],"points":1},
    {"number":22,"type":"gap_fill","noteTextBeforeGap":"in fingers","correctAnswer":"numbness","acceptedAnswers":["numbness"],"points":1},
    {"number":23,"type":"gap_fill","noteTextBeforeGap":"has practised ... long-term - recently commenced tai-chi","correctAnswer":"yoga","acceptedAnswers":["yoga"],"points":1},
    {"number":24,"type":"gap_fill","noteTextBeforeGap":"e.g. use of","correctAnswer":"resistance bands","acceptedAnswers":["resistance bands"],"points":1},
]

# ---- Part B (6 extracts, one MCQ each) ----
partB_questions = {
    25: {"stem":"The patient expresses a concern about","options":{"A":"having to make use of a mobility aid.","B":"being expected to mobilise without assistance.","C":"feeling unsteady when he's attempting to mobilise."},"correctAnswer":"C","transcript":partB_25,"context":"You hear a hospital nurse talking to a patient."},
    26: {"stem":"The patient may need some guidance in how to deal with","options":{"A":"the regular medication that he needs to take.","B":"ongoing therapy related to his long-term health needs.","C":"the sensitivity associated with a health condition he's developed."},"correctAnswer":"C","transcript":partB_26,"context":"You hear two community nurses conducting a patient handover."},
    27: {"stem":"What is the focus of today's session?","options":{"A":"comparing equipment used with patients of different ages","B":"gaining an awareness of how some equipment is used","C":"learning how best to organise some equipment"},"correctAnswer":"B","transcript":partB_27,"context":"You hear the beginning of a training session for nurses about to start work on a paediatric ward."},
    28: {"stem":"What is the priority for today's visit?","options":{"A":"helping the patient to regain independence in everyday tasks","B":"meeting a family member who has concerns about the patient","C":"ensuring that a mechanical device is appropriate for the patient"},"correctAnswer":"A","transcript":partB_28,"context":"You hear an occupational therapist briefing a trainee about a home visit that he's going to observe her making."},
    29: {"stem":"The patient's main concern about his medication is whether","options":{"A":"he's been prescribed the most effective dose.","B":"he's likely to experience long-term side effects.","C":"he's been taking it at the most appropriate time."},"correctAnswer":"B","transcript":partB_29,"context":"You hear a hospital pharmacist talking to a patient."},
    30: {"stem":"The patient is worried that she may have","options":{"A":"self-treated her toe in an inappropriate way.","B":"damaged a toe that she'd previously injured.","C":"triggered the resurgence of a health condition."},"correctAnswer":"C","transcript":partB_30,"context":"You hear a primary-care doctor talking to a patient."},
}

# ---- Part C questions ----
partC_ex1_questions = [
    {"number":31,"type":"multiple_choice_3","stem":"Dr Everall thinks misunderstandings about the role of cholesterol largely arise due to","options":{"A":"an imprecise use of the term in the media.","B":"inadequate explanations by health professionals.","C":"a lack of focus on its positive influences in research studies."},"correctAnswer":"A","points":1},
    {"number":32,"type":"multiple_choice_3","stem":"Dr Everall feels that using the words 'good' and 'bad' to describe types of cholesterol","options":{"A":"may be a useful way of clarifying a key point for patients.","B":"could encourage patients to find out more about the science.","C":"might lead patients to underestimate the complexity of the subject."},"correctAnswer":"A","points":1},
    {"number":33,"type":"multiple_choice_3","stem":"Dr Everall feels that some patients are reluctant to engage with the dangers of cholesterol because","options":{"A":"the standard investigations aren't generally available to them.","B":"they don't realise which social groups are most likely to be affected.","C":"no noticeable symptoms are associated with its gradual accumulation."},"correctAnswer":"C","points":1},
    {"number":34,"type":"multiple_choice_3","stem":"In terms of preventive medicine, Dr Everall mentions research that suggests high levels of cholesterol may result from","options":{"A":"the existence of an inherited predisposition.","B":"lifestyle factors that aren't usually associated with it.","C":"a range of modifiable behaviours particular to one age group."},"correctAnswer":"B","points":1},
    {"number":35,"type":"multiple_choice_3","stem":"What does Dr Everall say about the drug called Inclisiran?","options":{"A":"Its use could lead to considerable cost savings.","B":"Patients are likely to tolerate it better than existing options.","C":"Further research is needed to establish its full range of possible uses."},"correctAnswer":"B","points":1},
    {"number":36,"type":"multiple_choice_3","stem":"What point does Dr Everall make about the technology known as 'gene silencing'?","options":{"A":"Claims made about its potential uses need to be treated with caution.","B":"It works in a similar way to some other similar techniques.","C":"Wrong assumptions may sometimes be made about it."},"correctAnswer":"C","points":1},
]

partC_ex2_questions = [
    {"number":37,"type":"multiple_choice_3","stem":"Lianne feels that in response to increasing numbers of plus-size patients, nurses should","options":{"A":"take a lead in educating them about the risks.","B":"be proactive in investigating what lies behind the problem.","C":"remain focussed on providing them with the best possible service."},"correctAnswer":"C","points":1},
    {"number":38,"type":"multiple_choice_3","stem":"What attitude towards plus-size patients does Lianne find unacceptable?","options":{"A":"a belief that they're somehow to blame for their weight.","B":"a lack of interest in the medical reasons for their weight.","C":"a tendency to assume they've been trying to lose weight."},"correctAnswer":"A","points":1},
    {"number":39,"type":"multiple_choice_3","stem":"Lianne suggests adopting an approach to caring for patients that","options":{"A":"makes special provision for those defined as plus-size.","B":"is able to accommodate the needs of people of all sizes.","C":"involves a reassessment of what represents a typical size."},"correctAnswer":"B","points":1},
    {"number":40,"type":"multiple_choice_3","stem":"Lianne says the greatest problem with specialised equipment for plus-sized patients is often that","options":{"A":"a limited range is available for hospitals to choose from.","B":"hospitals lack the resources to invest in the quantities needed.","C":"the physical layout of hospitals can't accommodate them easily."},"correctAnswer":"C","points":1},
    {"number":41,"type":"multiple_choice_3","stem":"How does Lianne respond to the question about the safety issues presented by plus-size patients?","options":{"A":"She accepts that training in this area needs to be improved.","B":"She outlines some principles to apply to minimise any issues.","C":"She makes a case for increased levels of support for nursing staff."},"correctAnswer":"B","points":1},
    {"number":42,"type":"multiple_choice_3","stem":"When asked about the human dimension of caring for plus-size patients, Lianne underlines the value of","options":{"A":"involving patients in decisions about their everyday care.","B":"ensuring that patients appreciate any concerns staff may have.","C":"respecting the patient's wishes about how their size is referred to."},"correctAnswer":"A","points":1},
]

# Build Part B extracts (one question each)
partB_order = [25, 26, 27, 28, 29, 30]
partB_extracts = []
for i, qn in enumerate(partB_order, start=1):
    q = partB_questions[qn]
    partB_extracts.append({
        "extractNumber": i,
        "patientName": None,
        "professionalRole": None,
        "context": q["context"],
        "topic": None,
        "format": None,
        "readingTimeSeconds": 30,
        "transcript": q["transcript"],
        "accentCode": None,
        "questions": [{
            "number": qn,
            "type": "multiple_choice_3",
            "stem": q["stem"],
            "options": q["options"],
            "correctAnswer": q["correctAnswer"],
            "points": 1
        }]
    })

manifest = {
    "testTitle": "Listening Sample 1",
    "modeSupport": ["computer", "paper", "oet_home"],
    "strictMock": True,
    "partA": {"extracts": [
        {
            "extractNumber": 1,
            "patientName": "Hayley Dove",
            "professionalRole": "primary-care doctor",
            "context": None,
            "topic": None,
            "format": None,
            "readingTimeSeconds": 30,
            "transcript": partA_ex1_transcript,
            "accentCode": None,
            "questions": partA_ex1_questions
        },
        {
            "extractNumber": 2,
            "patientName": "Marvin Chainey",
            "professionalRole": "physiotherapist",
            "context": None,
            "topic": None,
            "format": None,
            "readingTimeSeconds": 30,
            "transcript": partA_ex2_transcript,
            "accentCode": None,
            "questions": partA_ex2_questions
        }
    ]},
    "partB": {"extracts": partB_extracts},
    "partC": {"extracts": [
        {
            "extractNumber": 1,
            "patientName": None,
            "professionalRole": None,
            "context": None,
            "topic": "cholesterol",
            "format": "presentation",
            "readingTimeSeconds": 90,
            "transcript": partC_ex1_transcript,
            "accentCode": None,
            "questions": partC_ex1_questions
        },
        {
            "extractNumber": 2,
            "patientName": None,
            "professionalRole": None,
            "context": None,
            "topic": "caring for obese or 'plus-size' patients",
            "format": "interview",
            "readingTimeSeconds": 90,
            "transcript": partC_ex2_transcript,
            "accentCode": None,
            "questions": partC_ex2_questions
        }
    ]}
}

# ---- VALIDATE ----
errors = []
a_ext = manifest["partA"]["extracts"]
b_ext = manifest["partB"]["extracts"]
c_ext = manifest["partC"]["extracts"]

if len(a_ext) != 2: errors.append("Part A extracts=%d (want 2)" % len(a_ext))
aq = sum(len(e["questions"]) for e in a_ext)
if aq != 24: errors.append("Part A questions=%d (want 24)" % aq)
if len(a_ext[0]["questions"]) != 12: errors.append("Part A ex1 != 12")
if len(a_ext[1]["questions"]) != 12: errors.append("Part A ex2 != 12")

if len(b_ext) != 6: errors.append("Part B extracts=%d (want 6)" % len(b_ext))
bq = sum(len(e["questions"]) for e in b_ext)
if bq != 6: errors.append("Part B questions=%d (want 6)" % bq)
for e in b_ext:
    if len(e["questions"]) != 1: errors.append("Part B ext %d has %d questions" % (e["extractNumber"], len(e["questions"])))

if len(c_ext) != 2: errors.append("Part C extracts=%d (want 2)" % len(c_ext))
cq = sum(len(e["questions"]) for e in c_ext)
if cq != 12: errors.append("Part C questions=%d (want 12)" % cq)
if len(c_ext[0]["questions"]) != 6: errors.append("Part C ex1 != 6")
if len(c_ext[1]["questions"]) != 6: errors.append("Part C ex2 != 6")

# Check question number sequence 1..42
allnums = []
for part in (a_ext, b_ext, c_ext):
    for e in part:
        for q in e["questions"]:
            allnums.append(q["number"])
if allnums != list(range(1, 43)):
    errors.append("Question numbers not 1..42 in order: %s" % allnums)

# Validate MCQ structure
for part in (b_ext, c_ext):
    for e in part:
        for q in e["questions"]:
            if q["type"] != "multiple_choice_3": errors.append("Q%d type wrong" % q["number"])
            if set(q["options"].keys()) != {"A", "B", "C"}: errors.append("Q%d options keys wrong" % q["number"])
            if q["correctAnswer"] not in ("A", "B", "C"): errors.append("Q%d correctAnswer bad" % q["number"])

# Validate gap_fill structure
for e in a_ext:
    for q in e["questions"]:
        if q["type"] != "gap_fill": errors.append("Q%d type wrong" % q["number"])
        if not q["correctAnswer"]: errors.append("Q%d no correctAnswer" % q["number"])
        if not isinstance(q["acceptedAnswers"], list) or len(q["acceptedAnswers"]) == 0: errors.append("Q%d acceptedAnswers bad" % q["number"])
        if not q.get("noteTextBeforeGap"): errors.append("Q%d no noteTextBeforeGap" % q["number"])

total = aq + bq + cq
if total != 42: errors.append("TOTAL=%d (want 42)" % total)

if errors:
    print("VALIDATION FAILED:")
    for er in errors:
        print("  -", er)
    raise SystemExit(1)

# Round-trip json to ensure validity
s = json.dumps(manifest, ensure_ascii=False, indent=2)
json.loads(s)

out_path = r"C:\Users\Administrator\Desktop\New OET Web App\.qa-artifacts\listening-1-manifest.json"
with open(out_path, "w", encoding="utf-8") as f:
    f.write(s)

print("OK")
print("A:%dx/%d B:%dx/%d C:%dx/%d total=%d" % (len(a_ext), aq, len(b_ext), bq, len(c_ext), cq, total))
print("written:", out_path)
