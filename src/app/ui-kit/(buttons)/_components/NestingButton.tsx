import React from "react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";
import { IconCode } from "@tabler/icons-react";

const NestingButton = () => {
  const buttonGroups = [
    {
      btnClass: "btn btn-secondary",
      toggleClass: "btn btn-secondary dropdown-toggle",
    },
    {
      btnClass: "btn btn-outline-secondary",
      toggleClass: "btn btn-outline-secondary dropdown-toggle",
    },
    {
      btnClass: "btn btn-light-secondary",
      toggleClass: "btn btn-light-secondary dropdown-toggle",
    },
  ];
  const checkboxItems = [
    { id: "btncheck1", label: "Checkbox 1" },
    { id: "btncheck2", label: "Checkbox 2" },
    { id: "btncheck3", label: "Checkbox 3" },
  ];

  const radioItems = [
    { id: "btnradio1", label: "Radio 1", defaultChecked: true },
    { id: "btnradio2", label: "Radio 2" },
    { id: "btnradio3", label: "Radio 3" },
  ];
  const [dropdownOpen, setDropdownOpen] = React.useState<boolean[]>(
    buttonGroups.map(() => false)
  );
  const [isNestingCodeVisible, setIsNestingCodeVisible] = React.useState(false);
  const [isRadioCodeVisible, setIsRadioCodeVisible] = React.useState(false);
  return (
    <>
      <Col lg={6}>
        <Card>
          <CardHeader className="code-header">
            <h5>Nesting</h5>
            <a
              href="#nestingexample"
              onClick={(e) => {
                e.preventDefault();
                setIsNestingCodeVisible(!isNestingCodeVisible);
              }}
            >
              <IconCode data-source="nesting" className="source" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <Row className="btn-responsive">
              {buttonGroups.map(({ btnClass, toggleClass }, index) => (
                <Col xs={12} className={index > 0 ? "mt-3" : ""} key={index}>
                  <div
                    className="btn-group"
                    role="group"
                    aria-label="Button group with nested dropdown"
                  >
                    <button type="button" className={btnClass}>
                      1
                    </button>
                    <button type="button" className={btnClass}>
                      2
                    </button>
                    <div className="btn-group" role="group">
                      <button
                        type="button"
                        className={toggleClass}
                        onClick={() => {
                          const newDropdownOpen = [...dropdownOpen];
                          newDropdownOpen[index] = !newDropdownOpen[index];
                          setDropdownOpen(newDropdownOpen);
                        }}
                      >
                        Dropdown
                      </button>
                      <ul
                        className={`dropdown-menu ${dropdownOpen[index] ? "show" : ""}`}
                      >
                        <li>
                          <a className="dropdown-item" href="#">
                            Dropdown link
                          </a>
                        </li>
                        <li>
                          <a className="dropdown-item" href="#">
                            Dropdown link
                          </a>
                        </li>
                      </ul>
                    </div>
                  </div>
                </Col>
              ))}
            </Row>
          </CardBody>
          <pre
            className={`nesting mt-3 ${isNestingCodeVisible ? "show" : "collapse"}`}
            id="nestingexample"
          >
            <code className="language-html">
              {`
<div class="row">
    <div class="col-md-6 col-lg-4 col-12">
        <div class="btn-group" role="group" aria-label="Button group with nested dropdown">
            <button type="button" class="btn btn-secondary">1</button>
            <button type="button" class="btn btn-secondary">2</button>

            <div class="btn-group" role="group">
                <button type="button" class="btn btn-secondary dropdown-toggle" aria-expanded="false">
                    Dropdown
                </button>
                <ul class="dropdown-menu">
                    <li><a class="dropdown-item" href="#">Dropdown link</a></li>
                    <li><a class="dropdown-item" href="#">Dropdown link</a></li>
                </ul>
            </div>
        </div>
    </div>

    <div class="col-md-6 col-lg-4 col-12">
        <div class="btn-group" role="group" aria-label="Button group with nested dropdown">
            <button type="button" class="btn btn-outline-secondary">1</button>
            <button type="button" class="btn btn-outline-secondary">2</button>

            <div class="btn-group" role="group">
                <button type="button" class="btn btn-outline-secondary dropdown-toggle" aria-expanded="false">
                    Dropdown
                </button>
                <ul class="dropdown-menu">
                    <li><a class="dropdown-item" href="#">Dropdown link</a></li>
                    <li><a class="dropdown-item" href="#">Dropdown link</a></li>
                </ul>
            </div>
        </div>
    </div>

    <div class="col-md-6 col-lg-4 col-12">
        <div class="btn-group" role="group" aria-label="Button group with nested dropdown">
            <button type="button" class="btn btn-light-secondary">1</button>
            <button type="button" class="btn btn-light-secondary">2</button>

            <div class="btn-group" role="group">
                <button type="button" class="btn btn-light-secondary dropdown-toggle" aria-expanded="false">
                    Dropdown
                </button>
                <ul class="dropdown-menu">
                    <li><a class="dropdown-item" href="#">Dropdown link</a></li>
                    <li><a class="dropdown-item" href="#">Dropdown link</a></li>
                </ul>
            </div>
        </div>
    </div>
</div>
`}
            </code>
          </pre>
        </Card>
      </Col>
      <Col lg={6}>
        <Card>
          <CardHeader className="code-header">
            <h5>Checkbox Radio</h5>
            <a
              href="#cheakradioexample"
              onClick={(e) => {
                e.preventDefault();
                setIsRadioCodeVisible(!isRadioCodeVisible);
              }}
            >
              <IconCode className="source" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <Row className="btn-responsive">
              <Col xs={12}>
                <div
                  className="btn-group"
                  role="group"
                  aria-label="Basic checkbox toggle button group"
                >
                  {checkboxItems.map(({ id, label }) => (
                    <React.Fragment key={id}>
                      <input type="checkbox" className="btn-check" id={id} />
                      <label className="btn btn-outline-secondary" htmlFor={id}>
                        {label}
                      </label>
                    </React.Fragment>
                  ))}
                </div>
              </Col>

              <Col xs={12} className="mt-3">
                <div
                  className="btn-group"
                  role="group"
                  aria-label="Basic radio toggle button group"
                >
                  {radioItems.map(({ id, label, defaultChecked }) => (
                    <React.Fragment key={id}>
                      <input
                        type="radio"
                        className="btn-check"
                        name="btnradio"
                        id={id}
                        defaultChecked={defaultChecked}
                      />
                      <label className="btn btn-outline-secondary" htmlFor={id}>
                        {label}
                      </label>
                    </React.Fragment>
                  ))}
                </div>
              </Col>

              <Col xs={12} className="mt-3">
                <div
                  className="btn-toolbar"
                  role="toolbar"
                  aria-label="Toolbar with button groups"
                >
                  <div
                    className="btn-group me-2"
                    role="group"
                    aria-label="First group"
                  >
                    {[1, 2, 3, 4].map((num) => (
                      <button
                        key={num}
                        type="button"
                        className="btn btn-secondary"
                      >
                        {num}
                      </button>
                    ))}
                  </div>
                  <div
                    className="btn-group"
                    role="group"
                    aria-label="Third group"
                  >
                    <button type="button" className="btn btn-secondary">
                      8
                    </button>
                  </div>
                </div>
              </Col>
            </Row>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default NestingButton;
