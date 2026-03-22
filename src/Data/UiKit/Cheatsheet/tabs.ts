export const tabsData = [
  {
    header: "tabs-primary",
    className: "app-tabs-primary",
    code: `
      <div class="card">
        <div class="card-body">
          <ul class="nav nav-tabs app-tabs-primary" role="tablist">
            <li class="nav-item" role="presentation">
              <button class="nav-link active" id="html-tab" type="button" role="tab" aria-controls="html-tab-pane" aria-selected="true">HTML</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="css-tab" type="button" role="tab" aria-controls="css-tab-pane" aria-selected="false">CSS</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="php-tab" type="button" role="tab" aria-controls="php-tab-pane" aria-selected="false">PHP</button>
            </li>
          </ul>
          <div class="tab-content" id="BasicContent">
            <div class="tab-pane fade show active" id="html-tab-pane" role="tabpanel" aria-labelledby="html-tab" tabindex="0">
              <p>Hypertext Markup Language is the standard markup language for documents designed to be displayed in a web browser.</p>
            </div>
            <div class="tab-pane fade" id="css-tab-pane" role="tabpanel" aria-labelledby="css-tab" tabindex="0">
              <p>Cascading Style Sheets (CSS) is a style sheet language used for describing the presentation of a document written in a markup language like HTML.</p>
              <p>CSS is a cornerstone technology of the World Wide Web, alongside HTML and JavaScript.</p>
            </div>
            <div class="tab-pane fade" id="php-tab-pane" role="tabpanel" aria-labelledby="php-tab" tabindex="0">
              <p>PHP is a popular general-purpose scripting language that is especially suited to web development.</p>
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "tabs-outline-primary",
    className: "tab-outline-primary",
    code: `
      <div class="card">
        <div class="card-body">
          <ul class="nav nav-tabs tab-outline-primary" role="tablist">
            <li class="nav-item" role="presentation">
              <button class="nav-link active" id="connect-tab" type="button" role="tab" aria-controls="connect-tab-pane" aria-selected="true">Connect</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="discover-tab" type="button" role="tab" aria-controls="discover-tab-pane" aria-selected="false">Discover</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="order-tab" type="button" role="tab" aria-controls="order-tab-pane" aria-selected="false">Orders</button>
            </li>
          </ul>
          <div class="tab-content" id="OutlineContent">
            <div class="tab-pane fade show active" id="connect-tab-pane" role="tabpanel" aria-labelledby="connect-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab one.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="discover-tab-pane" role="tabpanel" aria-labelledby="discover-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab two.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="order-tab-pane" role="tabpanel" aria-labelledby="order-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab three.</h6>
              ...
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "tabs-light",
    className: "tab-light-primary",
    code: `
      <div class="card">
        <div class="card-body">
          <ul class="nav nav-tabs tab-light-primary" role="tablist">
            <li class="nav-item" role="presentation">
              <button class="nav-link active" id="connect-tab" type="button" role="tab" aria-controls="connect-tab-pane" aria-selected="true">Connect</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="discover-tab" type="button" role="tab" aria-controls="discover-tab-pane" aria-selected="false">Discover</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="order-tab" type="button" role="tab" aria-controls="order-tab-pane" aria-selected="false">Orders</button>
            </li>
          </ul>
          <div class="tab-content" id="OutlineContent">
            <div class="tab-pane fade show active" id="connect-tab-pane" role="tabpanel" aria-labelledby="connect-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab one.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="discover-tab-pane" role="tabpanel" aria-labelledby="discover-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab two.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="order-tab-pane" role="tabpanel" aria-labelledby="order-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab three.</h6>
              ...
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "tabs-vertical",
    className: "vertical-tab",
    code: `
      <div class="card">
        <div class="card-body vertical-tab">
          <ul class="nav nav-tabs tab-light-secondary ms-3" role="tablist">
            <li class="nav-item" role="presentation">
              <button class="nav-link active" id="connect-tab" type="button" role="tab" aria-controls="connect-tab-pane" aria-selected="true">Connect</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="discover-tab" type="button" role="tab" aria-controls="discover-tab-pane" aria-selected="false">Discover</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="order-tab" type="button" role="tab" aria-controls="order-tab-pane" aria-selected="false">Orders</button>
            </li>
          </ul>
          <div class="tab-content" id="OutlineContent">
            <div class="tab-pane fade show active" id="connect-tab-pane" role="tabpanel" aria-labelledby="connect-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab one.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="discover-tab-pane" role="tabpanel" aria-labelledby="discover-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab two.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="order-tab-pane" role="tabpanel" aria-labelledby="order-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab three.</h6>
              ...
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "tabs-vertical-right",
    className: "vertical-right-tab",
    code: `
      <div class="card">
        <div class="card-body vertical-right-tab">
          <ul class="nav nav-tabs app-tabs-success" id="Outline" role="tablist">
            <li class="nav-item" role="presentation">
              <button class="nav-link active" id="connect-tab" type="button" role="tab" aria-controls="connect-tab-pane" aria-selected="true">Connect</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="discover-tab" type="button" role="tab" aria-controls="discover-tab-pane" aria-selected="false">Discover</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="order-tab" type="button" role="tab" aria-controls="order-tab-pane" aria-selected="false">Orders</button>
            </li>
          </ul>
          <div class="tab-content" id="OutlineContent">
            <div class="tab-pane fade show active" id="connect-tab-pane" role="tabpanel" aria-labelledby="connect-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab one.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="discover-tab-pane" role="tabpanel" aria-labelledby="discover-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab two.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="order-tab-pane" role="tabpanel" aria-labelledby="order-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab three.</h6>
              ...
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "tabs background",
    className: "tab-background",
    code: `
      <ul class="nav nav-tabs tab-primary bg-primary p-1" id="bg" role="tablist">
        <li class="nav-item" role="presentation">
          <button class="nav-link active" id="features-tab" type="button" role="tab" aria-controls="features-tab-pane" aria-selected="true"><i class="ti ti-disc pe-1 ps-1"></i> features</button>
        </li>
        <li class="nav-item" role="presentation">
          <button class="nav-link" id="history-tab" type="button" role="tab" aria-controls="history-tab-pane" aria-selected="false"><i class="ti ti-history pe-1 ps-1"></i>History</button>
        </li>
        <li class="nav-item" role="presentation">
          <button class="nav-link" id="reviews-tab" type="button" role="tab" aria-controls="reviews-tab-pane" aria-selected="false"><i class="ti ti-star pe-1 ps-1"></i>reviews</button>
        </li>
      </ul>
      <div class="tab-content" id="bgContent">
        <div class="tab-pane fade show active" id="features-tab-pane" role="tabpanel" aria-labelledby="features-tab" tabindex="0">
          <p>The idea is to use <code>:target</code> pseudoclassName to show tabs, use anchors with fragment identifiers to switch between them. The idea is to use pseudoclassName to show tabs, use anchors with fragment identifiers to switch between them.</p>
        </div>
        <div class="tab-pane fade" id="history-tab-pane" role="tabpanel" aria-labelledby="history-tab" tabindex="0">
          <ol>
            <li>Show only the last tab.</li>
            <li>If <code>:target</code> matches a tab, show it and hide all following siblings.</li>
            <li>Matches a tab, show it and hide all following siblings.</li>
          </ol>
        </div>
        <div class="tab-pane fade" id="reviews-tab-pane" role="tabpanel" aria-labelledby="reviews-tab" tabindex="0">
          <p>The idea is to use <code>:target</code> pseudoclassName to show tabs, use anchors with fragment identifiers to switch between them. The idea is to use pseudoclassName to show tabs, use anchors with fragment identifiers to switch between them.</p>
        </div>
      </div>
    `,
  },
  {
    header: "bottom-tab",
    className: "bottom-tab",
    code: `
      <div class="card">
        <div class="card-body bottom-tab">
          <ul class="nav nav-tabs app-tabs-success" role="tablist">
            <li class="nav-item" role="presentation">
              <button class="nav-link active" id="connect-tab" type="button" role="tab" aria-controls="connect-tab-pane" aria-selected="true">Connect</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="discover-tab" type="button" role="tab" aria-controls="discover-tab-pane" aria-selected="false">Discover</button>
            </li>
            <li class="nav-item" role="presentation">
              <button class="nav-link" id="order-tab" type="button" role="tab" aria-controls="order-tab-pane" aria-selected="false">Orders</button>
            </li>
          </ul>
          <div class="tab-content" id="OutlineContent">
            <div class="tab-pane fade show active" id="connect-tab-pane" role="tabpanel" aria-labelledby="connect-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab one.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="discover-tab-pane" role="tabpanel" aria-labelledby="discover-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab two.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="order-tab-pane" role="tabpanel" aria-labelledby="order-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab three.</h6>
              ...
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "tab-justify-content-around",
    className: "justify-content-around",
    code: `
      <div class="card">
        <div class="card-body">
          <ul class="nav nav-tabs app-tabs-dark justify-content-around" role="tablist">
            <li class="nav-item flex-fill" role="presentation">
              <button class="nav-link w-100 active" id="connect-tab" type="button" role="tab" aria-controls="connect-tab-pane" aria-selected="true">Connect</button>
            </li>
            <li class="nav-item flex-fill" role="presentation">
              <button class="nav-link w-100" id="discover-tab" type="button" role="tab" aria-controls="discover-tab-pane" aria-selected="false">Discover</button>
            </li>
            <li class="nav-item flex-fill" role="presentation">
              <button class="nav-link w-100" id="order-tab" type="button" role="tab" aria-controls="order-tab-pane" aria-selected="false">Orders</button>
            </li>
          </ul>
          <div class="tab-content" id="OutlineContent">
            <div class="tab-pane fade show active" id="connect-tab-pane" role="tabpanel" aria-labelledby="connect-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab one.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="discover-tab-pane" role="tabpanel" aria-labelledby="discover-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab two.</h6>
              ...
            </div>
            <div class="tab-pane fade" id="order-tab-pane" role="tabpanel" aria-labelledby="order-tab" tabindex="0">
              <h6 class="mb-1">This is the content of tab three.</h6>
              ...
            </div>
          </div>
        </div>
      </div>
    `,
  },
  {
    header: "image as nav link",
    className: "new-tab",
    code: `
    <div class="card">
      <div class="card-body">
                    <ul class="nav nav-tabs tab-light-secondary" id="lang-Light" role="tablist">
                      <li class="nav-item" role="presentation">
                        <button class="nav-link gap-2 active" id="lang-home-tab" type="button" role="tab"
                          aria-controls="lang-home-tab-pane" aria-selected="true">
                          <i class="flag-icon flag-icon-usa"></i>
                          USA
                        </button>
                      </li>
                      <li class="nav-item" role="presentation">
                        <button class="nav-link gap-2" id="lang-profile-tab" type="button" role="tab"
                          aria-controls="lang-profile-tab-pane" aria-selected="false">
                          <i class="flag-icon flag-icon-gbr"></i>
                          GBR
                        </button>
                      </li>
                      <li class="nav-item" role="presentation">
                        <button class="nav-link gap-2" id="lang-contact-tab" type="button" role="tab"
                          aria-controls="lang-contact-tab-pane" aria-selected="false">
                          <i class="flag-icon flag-icon-deu"></i>
                          DEU
                        </button>
                      </li>
                      <li class="nav-item" role="presentation">
                        <button class="nav-link gap-2" id="lang-about-tab" type="button" role="tab"
                          aria-controls="lang-about-tab-pane" aria-selected="false">
                          <i class="flag-icon flag-icon-lbr"></i>
                          LBR
                        </button>
                      </li>

                    </ul>
                    <div class="tab-content" id="lang-LightContent">
                      <div class="tab-pane fade show active" id="lang-home-tab-pane" role="tabpanel"
                        aria-labelledby="lang-home-tab" tabindex="0">
                        <p>The idea is to use <code>:target</code> pseudoclass to show tabs, use anchors with fragment
                          identifiers to switch between them. The idea is to use pseudoclass to show tabs, use anchors
                          with fragment identifiers to switch between them.</p>
                      </div>

                      <div class="tab-pane fade" id="lang-profile-tab-pane" role="tabpanel"
                        aria-labelledby="lang-profile-tab" tabindex="0">
                        <ol>
                          <li>Show only the last tab.</li>
                          <li>If <code>:target</code> matches a tab, show it and hide all following siblings.</li>
                          <li>Matches a tab, show it and hide all following siblings.</li>
                        </ol>
                      </div>

                      <div class="tab-pane fade" id="lang-contact-tab-pane" role="tabpanel"
                        aria-labelledby="lang-contact-tab" tabindex="0">
                        <p>The idea is to use <code>:target</code> pseudoclass to show tabs, use anchors with fragment
                          identifiers to switch between them. The idea is to use pseudoclass to show tabs, use anchors
                          with fragment identifiers to switch between them.</p>
                      </div>

                      <div class="tab-pane fade" id="lang-about-tab-pane" role="tabpanel"
                        aria-labelledby="lang-about-tab" tabindex="0">
                        <ol>
                          <li>Show only the last tab.</li>
                          <li>If <code>:target</code> matches a tab, show it and hide all following siblings.</li>
                          <li>Matches a tab, show it and hide all following siblings.</li>
                        </ol>
                      </div>

                    </div>
                  </div>
    </div>
  `,
  },
];
