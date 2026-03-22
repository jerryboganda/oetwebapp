import { useState } from "react";
import {
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
  Card,
  CardHeader,
  UncontrolledCollapse,
} from "reactstrap";
import { IconCode } from "@tabler/icons-react";

interface DropdownOption {
  label: string;
  value: string;
}

const DropdownExample = () => {
  const [dropdownOpen1, setDropdownOpen1] = useState(false);
  const [dropdownOpen2, setDropdownOpen2] = useState(false);

  const toggle1 = () => setDropdownOpen1((prevState) => !prevState);
  const toggle2 = () => setDropdownOpen2((prevState) => !prevState);

  const dropdownOptions: DropdownOption[] = [
    { label: "Action", value: "action" },
    { label: "Another action", value: "another-action" },
    { label: "Something else here", value: "something-else" },
  ];

  return (
    <div className="col-12">
      <Card>
        <CardHeader className="d-flex justify-content-between code-header card-header">
          <h5 className="txt-ellipsis">Single Button and Link Dropdown</h5>
          <a id="toggler" className="cursor-pointer">
            <IconCode data-source="blockbtn" className="source" size={36} />
          </a>
        </CardHeader>
        <div className="card-body d-flex flex-wrap gap-2">
          {/* Button Dropdown */}
          <Dropdown isOpen={dropdownOpen1} toggle={toggle1}>
            <DropdownToggle color="primary" caret>
              Dropdown button
            </DropdownToggle>
            <DropdownMenu>
              {dropdownOptions.map((option, index) => (
                <DropdownItem key={index} href="#">
                  {option.label}
                </DropdownItem>
              ))}
            </DropdownMenu>
          </Dropdown>

          {/* Link Dropdown */}
          <Dropdown isOpen={dropdownOpen2} toggle={toggle2}>
            <DropdownToggle color="secondary" caret>
              Dropdown link
            </DropdownToggle>
            <DropdownMenu>
              {dropdownOptions.map((option, index) => (
                <DropdownItem key={index} href="#">
                  {option.label}
                </DropdownItem>
              ))}
            </DropdownMenu>
          </Dropdown>
        </div>
      </Card>

      <UncontrolledCollapse toggler="#toggler">
        <pre className="mt-3">
          <code className="language-html">
            {`<Card>
  <CardHedaer ClassName="code-header">
    <h5>Single Button and Link Dropdown</h5>
  </CardHedaer>
  <CardBody class=" d-flex flex-wrap gap-2">
      <Dropdown isOpen={dropdownOpen1} toggle={toggle1}>
            <DropdownToggle color="primary" caret>
              Dropdown button
            </DropdownToggle>
            <DropdownMenu>
          ${dropdownOptions
            .map(
              (option) =>
                `        <DropdownItem  href="#">\n          ${option.label}\n        </DropdownItem>`
            )
            .join("\n")}
            </DropdownMenu>
          </Dropdown>
    <Dropdown isOpen={dropdownOpen2} toggle={toggle2}>
            <DropdownToggle color="secondary" caret>
              Dropdown link
            </DropdownToggle>
            <DropdownMenu>
              ${dropdownOptions
                .map(
                  (option) =>
                    `        <DropdownItem  href="#">\n          ${option.label}\n        </DropdownItem>`
                )
                .join("\n")}
            </DropdownMenu>
          </Dropdown>
  </CardBody>
</Card>`}
          </code>
        </pre>
      </UncontrolledCollapse>
    </div>
  );
};

export default DropdownExample;
