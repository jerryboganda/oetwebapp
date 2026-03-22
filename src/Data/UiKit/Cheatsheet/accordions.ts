export const accordionsData = [
  {
    header: "accordions",
    className: "app-accordion accordion-primary",
    code: `
      <div class="card">
        <div class="card-body">
          <div class="accordion app-accordion accordion-primary">
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button" type="button" aria-expanded="true" aria-controls="collapseOne">
                  Accordion Item #1
                </button>
              </h2>
              <div id="collapseOne" class="accordion-collapse collapse show">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="collapseTwo">
                  Accordion Item #2
                </button>
              </h2>
              <div id="collapseTwo" class="accordion-collapse collapse">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="collapseThree">
                  Accordion Item #3
                </button>
              </h2>
              <div id="collapseThree" class="accordion-collapse collapse">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "accordions-outline",
    className: "app-accordion accordion-outline-secondary",
    code: `
      <div class="card">
        <div class="card-body">
          <div class="accordion app-accordion accordion-outline-secondary">
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button" type="button" aria-expanded="true" aria-controls="collapseOneOutline">
                  Accordion Item #1
                </button>
              </h2>
              <div id="collapseOneOutline" class="accordion-collapse collapse show">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="collapseTwooutline">
                  Accordion Item #2
                </button>
              </h2>
              <div id="collapseTwooutline" class="accordion-collapse collapse">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="collapseThreeoutline">
                  Accordion Item #3
                </button>
              </h2>
              <div id="collapseThreeoutline" class="accordion-collapse collapse">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "accordions-light",
    className: "app-accordion accordion-light-success",
    code: `
      <div class="accordion app-accordion accordion-light-success" id="accordionFlushExample">
        <div class="accordion-item">
          <h2 class="accordion-header">
            <button class="accordion-button" type="button" aria-expanded="true" aria-controls="flush-collapseOne">
              Accordion Item #1
            </button>
          </h2>
          <div id="flush-collapseOne" class="accordion-collapse show">
            <div class="accordion-body">
              Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush</code> className. This is the first item's accordion body.
            </div>
          </div>
        </div>
        <div class="accordion-item">
          <h2 class="accordion-header">
            <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="flush-collapseTwo">
              Accordion Item #2
            </button>
          </h2>
          <div id="flush-collapseTwo" class="accordion-collapse collapse">
            <div class="accordion-body">
              Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush</code> className. This is the second item's accordion body. Let's imagine this being filled with some actual content.
            </div>
          </div>
        </div>
        <div class="accordion-item">
          <h2 class="accordion-header">
            <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="flush-collapseThree">
              Accordion Item #3
            </button>
          </h2>
          <div id="flush-collapseThree" class="accordion-collapse collapse">
            <div class="accordion-body">
              Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush</code> className. This is the third item's accordion body. Nothing more exciting happening here in terms of content, but just filling up the space to make it look, at least at first glance, a bit more representative of how this would look in a real-world application.
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "accordions",
    className: "app-accordion accordion-primary",
    code: `
      <div class="card">
        <div class="card-body">
          <div class="accordion app-accordion accordion-primary">
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button" type="button" aria-expanded="true" aria-controls="collapseOne">
                  Accordion Item #1
                </button>
              </h2>
              <div id="collapseOne" class="accordion-collapse collapse show">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="collapseTwo">
                  Accordion Item #2
                </button>
              </h2>
              <div id="collapseTwo" class="accordion-collapse collapse" >
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="collapseThree">
                  Accordion Item #3
                </button>
              </h2>
              <div id="collapseThree" class="accordion-collapse collapse">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "accordions-outline",
    className: "app-accordion accordion-outline-secondary",
    code: `
      <div class="card">
        <div class="card-body">
          <div class="accordion app-accordion accordion-outline-secondary">
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button" type="button" aria-expanded="true" aria-controls="collapseOneOutline">
                  Accordion Item #1
                </button>
              </h2>
              <div id="collapseOneOutline" class="accordion-collapse collapse show">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="collapseTwooutline">
                  Accordion Item #2
                </button>
              </h2>
              <div id="collapseTwooutline" class="accordion-collapse collapse">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
            <div class="accordion-item">
              <h2 class="accordion-header">
                <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="collapseThreeoutline">
                  Accordion Item #3
                </button>
              </h2>
              <div id="collapseThreeoutline" class="accordion-collapse collapse">
                <div class="accordion-body">
                  ...
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "accordions-light",
    className: "app-accordion accordion-light-success",
    code: `
      <div class="accordion app-accordion accordion-light-success" id="accordionFlushExample">
        <div class="accordion-item">
          <h2 class="accordion-header">
            <button class="accordion-button" type="button" aria-expanded="true" aria-controls="flush-collapseOne">
              Accordion Item #1
            </button>
          </h2>
          <div id="flush-collapseOne" class="accordion-collapse show">
            <div class="accordion-body">
              Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush</code> className. This is the first item's accordion body.
            </div>
          </div>
        </div>
        <div class="accordion-item">
          <h2 class="accordion-header">
            <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="flush-collapseTwo">
              Accordion Item #2
            </button>
          </h2>
          <div id="flush-collapseTwo" class="accordion-collapse collapse">
            <div class="accordion-body">
              Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush</code> className. This is the second item's accordion body. Let's imagine this being filled with some actual content.
            </div>
          </div>
        </div>
        <div class="accordion-item">
          <h2 class="accordion-header">
            <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="flush-collapseThree">
              Accordion Item #3
            </button>
          </h2>
          <div id="flush-collapseThree" class="accordion-collapse collapse">
            <div class="accordion-body">
              Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush</code> className. This is the third item's accordion body. Nothing more exciting happening here in terms of content, but just filling up the space to make it look, at least at first glance, a bit more representative of how this would look in a real-world application.
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "No icon indicators",
    className: "app-accordion accordion-light-success",
    code: `
      <div class="card">
          <div class="card-body">
            <div class="accordion app-accordion accordion-light-danger app-accordion-no-icon" id="accordionnoiconExample">
              <div class="accordion-item">
                <h2 class="accordion-header">
                  <button class="accordion-button" type="button" aria-expanded="true" aria-controls="noicon-collapseOne">
                    <i class="ti ti-message-report me-1 f-s-20"></i> What happens if I just pay my ticket?
                  </button>
                </h2>
                <div id="noicon-collapseOne" class="accordion-collapse collapse show">
                  <div class="accordion-body">
                    Paying your ticket without contesting it can result in a conviction on your driving record. This can have long-lasting consequences such as increased insurance rates, driver's license suspension, employment restrictions, and additional fines imposed by your state's DMV.
                  </div>
                </div>
              </div>
              <div class="accordion-item">
                <h2 class="accordion-header">
                  <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="noicon-collapseTwo">
                    <i class="ti ti-ticket me-1 f-s-20"></i> Can this ticket affect my job?
                  </button>
                </h2>
                <div id="noicon-collapseTwo" class="accordion-collapse collapse">
                  <div class="accordion-body">
                    Absolutely, tickets recorded on your driving history can result in disqualification from driving commercially or obtaining a commercial driver's license (CDL). They can also disqualify you from driving for ride-sharing services such as Uber or Lyft, which require a clean driving record.
                  </div>
                </div>
              </div>
              <div class="accordion-item">
                <h2 class="accordion-header">
                  <button class="accordion-button collapsed" type="button" aria-expanded="false" aria-controls="noicon-collapseThree">
                    <i class="ti ti-transition-bottom me-1 f-s-20"></i> How long does it take to resolve my case?
                  </button>
                </h2>
                <div id="noicon-collapseThree" class="accordion-collapse collapse">
                  <div class="accordion-body">
                    It really depends on the court your ticket landed in. Some courts move faster than others, but on average, it could take about 1-3 months. This is perfectly normal in the legal process.
                  </div>
                </div>
              </div>
            </div>
          </div>
    </div>
    `,
  },
];
