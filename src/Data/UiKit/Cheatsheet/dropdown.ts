export const dropdownData = [
  {
    className: "app-dropdown",
    header: "app-dropdown",
    code: `<div class="card">
  <div class="card-body d-flex flex-wrap gap-2">
    <div class="app-dropdown">
      <button class="btn btn-primary border-0 " type="button" aria-expanded="false">
        Clickable Submenu
      </button>
      <div class="dropdown-menu mb-3">
        <a href="#" class="dropdown-item">
          <span> Action</span>
          <i class="ti ti-user-plus ms-auto"></i>
        </a>
        <hr class="dropdown-divider">
        <a class="dropdown-item border-0" role="button" aria-expanded="false">
          <span>More Option</span>
          <i class="ti ti-arrow-badge-right ms-auto"></i>
        </a>
        <div class="dropdown-menu sub-menu">
          <a href="#" class="dropdown-item">
            <span> Action</span>
            <i class="ti ti-user-plus ms-auto"></i>
          </a>
          <a href="#" class="dropdown-item">
            <span>Another action</span>
            <i class="ti ti-circles-relation ms-auto"></i>
          </a>
        </div>
      </div>
    </div>

    <div class="hover-dropdown app-hover-dropdown">
      <button class="btn btn-primary dropdown-toggle waves-effect waves-light" aria-expanded="false">
        Hoverable Dropdown
      </button>
      <ul class="dropdown-menu bg-dark">
        <li class="dropdown-item">
          <a href="#">
            <span> Action</span>
          </a>
          <i class="ti ti-user-plus ms-auto"></i>
        </li>
        <li class="dropdown-item">
          <a href="#">
            <span>Another action</span>
          </a>
          <i class="ti ti-circles-relation ms-auto"></i>
        </li>
        <li class="dropdown-item">
          <a href="#">
            <span>Something else here</span>
          </a>
          <i class="ti ti-message-circle ms-auto"></i>
        </li>
        <li>
          <hr class="dropdown-divider">
        </li>
        <li class="hover-dropdown">
          <a role="button" class="dropdown-item waves-effect waves-light" aria-expanded="false">
            More Option
          </a>
          <ul class="dropdown-menu hover-submenu bg-dark">
            <li class="dropdown-item">
              <a href="#">
                <span> Action</span>
              </a>
              <i class="ti ti-user-plus ms-auto"></i>
            </li>
            <li class="dropdown-item">
              <a href="#">
                <span>Another action</span>
              </a>
              <i class="ti ti-circles-relation ms-auto"></i>
            </li>
            <li class="dropdown-item">
              <a href="#">
                <span>Something else here</span>
              </a>
              <i class="ti ti-message-circle ms-auto"></i>
            </li>
          </ul>
        </li>

      </ul>
    </div>

  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown primary",
    code: `<div class="btn-group btn-rtl">
  <button type="button" class="btn btn-primary dropdown-toggle" aria-haspopup="true" aria-expanded="false">
    primary <i class="mdi mdi-chevron-down"></i>
  </button>
  <div class="dropdown-menu">
    <a class="dropdown-item" href="#">Action</a>
    <a class="dropdown-item" href="#">Another action</a>
    <a class="dropdown-item" href="#">Something else here</a>
  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown",
    code: `<div class="card">
  <div class="card-body d-flex flex-wrap gap-2">
    <div class="app-dropdown">
      <button class="btn btn-primary border-0 " type="button" aria-expanded="false">
        Clickable Submenu
      </button>
      <div class="dropdown-menu mb-3">
        <a href="#" class="dropdown-item">
          <span> Action</span>
          <i class="ti ti-user-plus ms-auto"></i>
        </a>
        <hr class="dropdown-divider">
        <a class="dropdown-item border-0" role="button" aria-expanded="false">
          <span>More Option</span>
          <i class="ti ti-arrow-badge-right ms-auto"></i>
        </a>
        <div class="dropdown-menu sub-menu">
          <a href="#" class="dropdown-item">
            <span> Action</span>
            <i class="ti ti-user-plus ms-auto"></i>
          </a>
          <a href="#" class="dropdown-item">
            <span>Another action</span>
            <i class="ti ti-circles-relation ms-auto"></i>
          </a>
        </div>
      </div>
    </div>

    <div class="hover-dropdown app-hover-dropdown">
      <button class="btn btn-primary dropdown-toggle waves-effect waves-light" data-trigger="hover" aria-expanded="false">
        Hoverable Dropdown
      </button>
      <ul class="dropdown-menu bg-dark">
        <li class="dropdown-item">
          <a href="#">
            <span> Action</span>
          </a>
          <i class="ti ti-user-plus ms-auto"></i>
        </li>
        <li class="dropdown-item">
          <a href="#">
            <span>Another action</span>
          </a>
          <i class="ti ti-circles-relation ms-auto"></i>
        </li>
        <li class="dropdown-item">
          <a href="#">
            <span>Something else here</span>
          </a>
          <i class="ti ti-message-circle ms-auto"></i>
        </li>
        <li>
          <hr class="dropdown-divider">
        </li>
        <li class="hover-dropdown">
          <a role="button" class="dropdown-item waves-effect waves-light" data-trigger="hover" aria-expanded="false">
            More Option
          </a>
          <ul class="dropdown-menu hover-submenu bg-dark">
            <li class="dropdown-item">
              <a href="#">
                <span> Action</span>
              </a>
              <i class="ti ti-user-plus ms-auto"></i>
            </li>
            <li class="dropdown-item">
              <a href="#">
                <span>Another action</span>
              </a>
              <i class="ti ti-circles-relation ms-auto"></i>
            </li>
            <li class="dropdown-item">
              <a href="#">
                <span>Something else here</span>
              </a>
              <i class="ti ti-message-circle ms-auto"></i>
            </li>
          </ul>
        </li>

      </ul>
    </div>

  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown primary",
    code: `<div class="btn-group btn-rtl">
  <button type="button" class="btn btn-primary dropdown-toggle" aria-haspopup="true" aria-expanded="false">
    primary <i class="mdi mdi-chevron-down"></i>
  </button>
  <div class="dropdown-menu">
    <a class="dropdown-item" href="#">Action</a>
    <a class="dropdown-item" href="#">Another action</a>
    <a class="dropdown-item" href="#">Something else here</a>
  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown secondary",
    code: `<div class="btn-group btn-rtl">
  <button type="button" class="btn btn-secondary dropdown-toggle" aria-haspopup="true" aria-expanded="false">
    secondary <i class="mdi mdi-chevron-down"></i>
  </button>
  <div class="dropdown-menu">
    <a class="dropdown-item" href="#">Action</a>
    <a class="dropdown-item" href="#">Another action</a>
    <a class="dropdown-item" href="#">Something else here</a>
  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown success",
    code: `<div class="btn-group btn-rtl">
  <button type="button" class="btn btn-success dropdown-toggle" aria-haspopup="true" aria-expanded="false">
    success <i class="mdi mdi-chevron-down"></i>
  </button>
  <div class="dropdown-menu">
    <a class="dropdown-item" href="#">Action</a>
    <a class="dropdown-item" href="#">Another action</a>
    <a class="dropdown-item" href="#">Something else here</a>
  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown danger",
    code: `<div class="btn-group btn-rtl">
  <button type="button" class="btn btn-danger dropdown-toggle" aria-haspopup="true" aria-expanded="false">
    danger <i class="mdi mdi-chevron-down"></i>
  </button>
  <div class="dropdown-menu">
    <a class="dropdown-item" href="#">Action</a>
    <a class="dropdown-item" href="#">Another action</a>
    <a class="dropdown-item" href="#">Something else here</a>
  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown warning",
    code: `<div class="btn-group btn-rtl">
  <button type="button" class="btn btn-warning dropdown-toggle" aria-haspopup="true" aria-expanded="false">
    warning <i class="mdi mdi-chevron-down"></i>
  </button>
  <div class="dropdown-menu">
    <a class="dropdown-item" href="#">Action</a>
    <a class="dropdown-item" href="#">Another action</a>
    <a class="dropdown-item" href="#">Something else here</a>
  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown info",
    code: `<div class="btn-group btn-rtl">
  <button type="button" class="btn btn-info dropdown-toggle" aria-haspopup="true" aria-expanded="false">
    info <i class="mdi mdi-chevron-down"></i>
  </button>
  <div class="dropdown-menu">
    <a class="dropdown-item" href="#">Action</a>
    <a class="dropdown-item" href="#">Another action</a>
    <a class="dropdown-item" href="#">Something else here</a>
  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown light",
    code: `<div class="btn-group btn-rtl">
  <button type="button" class="btn btn-light dropdown-toggle" aria-haspopup="true" aria-expanded="false">
    light <i class="mdi mdi-chevron-down"></i>
  </button>
  <div class="dropdown-menu">
    <a class="dropdown-item" href="#">Action</a>
    <a class="dropdown-item" href="#">Another action</a>
    <a class="dropdown-item" href="#">Something else here</a>
  </div>
</div>`,
  },
  {
    className: "app-dropdown",
    header: "app-dropdown dark",
    code: `<div class="btn-group btn-rtl">
  <button type="button" class="btn btn-dark dropdown-toggle" aria-haspopup="true" aria-expanded="false">
    dark <i class="mdi mdi-chevron-down"></i>
  </button>
  <div class="dropdown-menu">
    <a class="dropdown-item" href="#">Action</a>
    <a class="dropdown-item" href="#">Another action</a>
    <a class="dropdown-item" href="#">Something else here</a>
  </div>
</div>`,
  },
  {
    className: "dropdown-menu",
    header: "dropdown-menu menu-primary",
    code: `<div class="app-dropdown">
  <button class="btn border-0 icon-btn" type="button" aria-expanded="false">
    <i class="ti ti-dots"></i>
  </button>
  <ul class="dropdown-menu menu-primary show">
    <li class="dropdown-item d-flex justify-content-between">
      <span> Action</span>
      <i class="ti ti-user-plus"></i>
    </li>
    <li class="dropdown-item d-flex justify-content-between">
      <span>Another action</span>
      <i class="ti ti-circles-relation"></i>
    </li>
    <li class="dropdown-item d-flex justify-content-between">
      <span>Something else here</span>
      <i class="ti ti-message-circle"></i>
    </li>
    <li class="dropdown-divider"></li>
    <li class="dropdown-item d-flex justify-content-between">
      <span>Settings</span>
      <i class="ti ti-settings"></i>
    </li>
  </ul>
</div>`,
  },
  {
    className: "dropdown-menu",
    header: "dropdown-menu menu-secondary",
    code: `<div class="app-dropdown">
  <button class="btn border-0 icon-btn" type="button" aria-expanded="false">
    <i class="ti ti-dots"></i>
  </button>
  <ul class="dropdown-menu menu-secondary show">
    <li class="dropdown-item d-flex justify-content-between">
      <span> Action</span>
      <i class="ti ti-user-plus"></i>
    </li>
    <li class="dropdown-item d-flex justify-content-between">
      <span>Another action</span>
      <i class="ti ti-circles-relation"></i>
    </li>
    <li class="dropdown-item d-flex justify-content-between">
      <span>Something else here</span>
      <i class="ti ti-message-circle"></i>
    </li>
    <li class="dropdown-divider"></li>
    <li class="dropdown-item d-flex justify-content-between">
      <span>Settings</span>
      <i class="ti ti-settings"></i>
    </li>
  </ul>
</div>`,
  },
  {
    className: "dropdown-menu",
    header: "dropdown-menu menu-warning",
    code: `
      <div class="app-dropdown">
        <button class="btn border-0 icon-btn" type="button" aria-expanded="false">
          <i class="ti ti-dots"></i>
        </button>
        <ul class="dropdown-menu menu-warning show">
          <li class="dropdown-item d-flex justify-content-between">
            <span> Action</span>
            <i class="ti ti-user-plus"></i>
          </li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Another action</span>
            <i class="ti ti-circles-relation"></i>
          </li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Something else here</span>
            <i class="ti ti-message-circle"></i>
          </li>
          <li class="dropdown-divider"></li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Settings</span>
            <i class="ti ti-settings"></i>
          </li>
        </ul>
      </div>
    `,
  },
  {
    className: "dropdown-menu",
    header: "dropdown-menu menu-info",
    code: `
      <div class="app-dropdown">
        <button class="btn border-0 icon-btn" type="button" aria-expanded="false">
          <i class="ti ti-dots"></i>
        </button>
        <ul class="dropdown-menu menu-info show">
          <li class="dropdown-item d-flex justify-content-between">
            <span> Action</span>
            <i class="ti ti-user-plus"></i>
          </li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Another action</span>
            <i class="ti ti-circles-relation"></i>
          </li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Something else here</span>
            <i class="ti ti-message-circle"></i>
          </li>
          <li class="dropdown-divider"></li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Settings</span>
            <i class="ti ti-settings"></i>
          </li>
        </ul>
      </div>
    `,
  },
  {
    className: "dropdown-menu",
    header: "dropdown-menu menu-light",
    code: `
      <div class="app-dropdown">
        <button class="btn border-0 icon-btn" type="button" aria-expanded="false">
          <i class="ti ti-dots"></i>
        </button>
        <ul class="dropdown-menu menu-light show">
          <li class="dropdown-item d-flex justify-content-between">
            <span> Action</span>
            <i class="ti ti-user-plus"></i>
          </li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Another action</span>
            <i class="ti ti-circles-relation"></i>
          </li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Something else here</span>
            <i class="ti ti-message-circle"></i>
          </li>
          <li class="dropdown-divider"></li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Settings</span>
            <i class="ti ti-settings"></i>
          </li>
        </ul>
      </div>
    `,
  },
  {
    className: "dropdown-menu",
    header: "dropdown-menu menu-dark",
    code: `
      <div class="app-dropdown">
        <button class="btn border-0 icon-btn" type="button" aria-expanded="false">
          <i class="ti ti-dots"></i>
        </button>
        <ul class="dropdown-menu menu-dark show">
          <li class="dropdown-item d-flex justify-content-between">
            <span> Action</span>
            <i class="ti ti-user-plus"></i>
          </li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Another action</span>
            <i class="ti ti-circles-relation"></i>
          </li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Something else here</span>
            <i class="ti ti-message-circle"></i>
          </li>
          <li class="dropdown-divider"></li>
          <li class="dropdown-item d-flex justify-content-between">
            <span>Settings</span>
            <i class="ti ti-settings"></i>
          </li>
        </ul>
      </div>
    `,
  },
];
