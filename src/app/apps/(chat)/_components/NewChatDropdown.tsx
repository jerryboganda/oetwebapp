import React from "react";
import { IconBrandHipchat, IconPhoneCall, IconPlus } from "@tabler/icons-react";
import {
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownToggle,
} from "reactstrap";

const NewChatDropdown = () => {
  const [dropdownOpen, setDropdownOpen] = React.useState(false);
  return (
    <>
      <div className="float-end">
        <div className="btn-group dropdown-icon-none">
          <Dropdown
            isOpen={dropdownOpen}
            toggle={() => setDropdownOpen(!dropdownOpen)}
          >
            <DropdownToggle
              tag="button"
              className="btn btn-primary icon-btn b-r-22"
              type="button"
            >
              <IconPlus size={18} />
            </DropdownToggle>
            <DropdownMenu>
              <DropdownItem>
                <IconBrandHipchat size={18} />{" "}
                <span className="f-s-13">New Chat</span>
              </DropdownItem>
              <DropdownItem>
                <IconPhoneCall size={18} />{" "}
                <span className="f-s-13">New Contact</span>
              </DropdownItem>
            </DropdownMenu>
          </Dropdown>
        </div>
      </div>
    </>
  );
};

export default NewChatDropdown;
