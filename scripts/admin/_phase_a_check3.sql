SELECT column_name FROM information_schema.columns
WHERE table_schema='public' AND table_name='MockBundleSections' ORDER BY ordinal_position;

-- Any mock bundle references to our delete targets?
SELECT "Id", "ContentPaperId" FROM "MockBundleSections"
WHERE "ContentPaperId" IN (
  '30b2245a46cf45e39e4e72f0de77ff63',
  '167124ddf8ec412e948df16dd0ea9784',
  '1322a10d2e4644378ffdb131c3c2cb71',
  '06ed32dd4bce4800bbe84c16ec8507ca',
  '16203e2a53344e598c532d67bb8d4cb8',
  '51900b7211b84a8dbeb1d336b6e7c14a',
  'b8e0e9def00a4dd192beb08a5121deb9'
);

-- Any learner attempts on the delete targets?
SELECT "PaperId", COUNT(*) AS attempts FROM "ListeningAttempts"
WHERE "PaperId" IN (
  '30b2245a46cf45e39e4e72f0de77ff63',
  '167124ddf8ec412e948df16dd0ea9784',
  '1322a10d2e4644378ffdb131c3c2cb71',
  '06ed32dd4bce4800bbe84c16ec8507ca',
  '16203e2a53344e598c532d67bb8d4cb8',
  '51900b7211b84a8dbeb1d336b6e7c14a',
  'b8e0e9def00a4dd192beb08a5121deb9'
) GROUP BY "PaperId";

-- Any study plan items?
SELECT "ContentPaperId", COUNT(*) FROM "StudyPlanItems"
WHERE "ContentPaperId" IN (
  '30b2245a46cf45e39e4e72f0de77ff63',
  '167124ddf8ec412e948df16dd0ea9784',
  '1322a10d2e4644378ffdb131c3c2cb71',
  '06ed32dd4bce4800bbe84c16ec8507ca',
  '16203e2a53344e598c532d67bb8d4cb8',
  '51900b7211b84a8dbeb1d336b6e7c14a',
  'b8e0e9def00a4dd192beb08a5121deb9'
) GROUP BY "ContentPaperId";
