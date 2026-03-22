import { useState } from "react";
import {
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
  Card,
  CardHeader,
  CardBody,
  UncontrolledCollapse,
  ButtonGroup,
  Button,
} from "reactstrap";
import { IconCode } from "@tabler/icons-react";

interface DropdownOption {
  label: string;
  value: string;
}

const DropdownOutlineVariants = () => {
  // State for each dropdown
  const [dropdownOpen1, setDropdownOpen1] = useState(false);
  const [dropdownOpen2, setDropdownOpen2] = useState(false);
  const [dropdownOpen3, setDropdownOpen3] = useState(false);
  const [dropdownOpen4, setDropdownOpen4] = useState(false);

  // Toggle functions
  const toggle1 = () => setDropdownOpen1((prev) => !prev);
  const toggle2 = () => setDropdownOpen2((prev) => !prev);
  const toggle3 = () => setDropdownOpen3((prev) => !prev);
  const toggle4 = () => setDropdownOpen4((prev) => !prev);

  // Dropdown options
  const dropdownOptions: DropdownOption[] = [
    { label: "Action", value: "action" },
    { label: "Another action", value: "another-action" },
    { label: "Something else here", value: "something-else" },
    { label: "divider", value: "divider" },
    { label: "Separated link", value: "separated-link" },
  ];

  return (
    <div className="col-12 outline-btn">
      <Card>
        <CardHeader className="d-flex justify-content-between code-header card-header">
          <h5 className="txt-ellipsis">Dropup Variation</h5>
          <a id="toggler3" className="cursor-pointer">
            <IconCode data-source="blockbtn" className="source" size={36} />
          </a>
        </CardHeader>
        <CardBody className="d-flex flex-wrap gap-2">
          <ButtonGroup className="btn-rtl">
            <Button color="light-primary">Primary</Button>
            <Dropdown
              isOpen={dropdownOpen1}
              toggle={toggle1}
              direction={"down"}
            >
              <DropdownToggle
                color="light-primary"
                className="dropdown-toggle dropdown-toggle-split p-2 dropdown rounded-end outline-variant"
              >
                <span className="visually-hidden">Toggle Dropdown</span>
              </DropdownToggle>
              <DropdownMenu>
                {dropdownOptions.map((option, index) =>
                  option.value === "divider" ? (
                    <DropdownItem divider key={index} />
                  ) : (
                    <DropdownItem key={index} href="#">
                      {option.label}
                    </DropdownItem>
                  )
                )}
              </DropdownMenu>
            </Dropdown>
          </ButtonGroup>

          {/* Secondary */}
          <ButtonGroup className="btn-rtl">
            <Button color="light-secondary">Split Dropup</Button>
            <Dropdown isOpen={dropdownOpen2} toggle={toggle2} direction={"up"}>
              <DropdownToggle
                color="light-secondary"
                className="btn btn-light-secondary dropdown-toggle dropdown-toggle-split p-2 dropdown rounded-end outline-variant"
              >
                <span className="visually-hidden">Toggle Dropdown</span>
              </DropdownToggle>
              <DropdownMenu>
                {dropdownOptions.map((option, index) =>
                  option.value === "divider" ? (
                    <DropdownItem divider key={index} />
                  ) : (
                    <DropdownItem key={index} href="#">
                      {option.label}
                    </DropdownItem>
                  )
                )}
              </DropdownMenu>
            </Dropdown>
          </ButtonGroup>

          {/* Success */}
          <ButtonGroup className="btn-rtl">
            <Button color="light-success">Splite Dropupend</Button>
            <Dropdown isOpen={dropdownOpen3} toggle={toggle3} direction={"end"}>
              <DropdownToggle
                color="light-success"
                className="dropdown-toggle dropdown-toggle-split p-2 dropdown rounded-end outline-variant"
              >
                <span className="visually-hidden">Toggle Dropdown</span>
              </DropdownToggle>
              <DropdownMenu>
                {dropdownOptions.map((option, index) =>
                  option.value === "divider" ? (
                    <DropdownItem divider key={index} />
                  ) : (
                    <DropdownItem key={index} href="#">
                      {option.label}
                    </DropdownItem>
                  )
                )}
              </DropdownMenu>
            </Dropdown>
          </ButtonGroup>

          {/* Danger */}
          <ButtonGroup className="btn-rtl">
            <Button color="light-danger">Splite Dropupstart</Button>
            <Dropdown
              isOpen={dropdownOpen4}
              toggle={toggle4}
              direction={"start"}
            >
              <DropdownToggle
                color="light-danger"
                className="dropdown-toggle dropdown-toggle-split p-2 dropdown rounded-end outline-variant"
              >
                <span className="visually-hidden">Toggle Dropdown</span>
              </DropdownToggle>
              <DropdownMenu>
                {dropdownOptions.map((option, index) =>
                  option.value === "divider" ? (
                    <DropdownItem divider key={index} />
                  ) : (
                    <DropdownItem key={index} href="#">
                      {option.label}
                    </DropdownItem>
                  )
                )}
              </DropdownMenu>
            </Dropdown>
          </ButtonGroup>
        </CardBody>
      </Card>

      <UncontrolledCollapse toggler="#toggler3">
        <pre className="mt-3">
          <code className="language-html">
            {`<Card>
  <CardHeader className="code-header">
    <h5>Dropdown Outline Variants</h5>
  </CardHeader>
  <CardBody className="d-flex flex-wrap gap-2">
    {/* Primary */}
    <ButtonGroup className="btn-rtl">
      <Button outline color="primary">Primary</Button>
      <Dropdown isOpen={dropdownOpen1} toggle={toggle1}>
        <DropdownToggle outline color="primary" className="dropdown-toggle-split p-2">
          <span className="visually-hidden">Toggle Dropdown</span>
        </DropdownToggle>
        <DropdownMenu>
${dropdownOptions
  .map((option) =>
    option.value === "divider"
      ? `          <DropdownItem divider />`
      : `          <DropdownItem href="#">${option.label}</DropdownItem>`
  )
  .join("\n")}
        </DropdownMenu>
      </Dropdown>
    </ButtonGroup>

    {/* Secondary */}
    <ButtonGroup className="btn-rtl">
      <Button outline color="secondary">Secondary</Button>
      <Dropdown isOpen={dropdownOpen2} toggle={toggle2}>
        <DropdownToggle outline color="secondary" className="dropdown-toggle-split p-2">
          <span className="visually-hidden">Toggle Dropdown</span>
        </DropdownToggle>
        <DropdownMenu>
${dropdownOptions
  .map((option) =>
    option.value === "divider"
      ? `          <DropdownItem divider />`
      : `          <DropdownItem href="#">${option.label}</DropdownItem>`
  )
  .join("\n")}
        </DropdownMenu>
      </Dropdown>
    </ButtonGroup>

    {/* Repeat for other variants... */}
  </CardBody>
</Card>`}
          </code>
        </pre>
      </UncontrolledCollapse>
    </div>
  );
};

export default DropdownOutlineVariants;
