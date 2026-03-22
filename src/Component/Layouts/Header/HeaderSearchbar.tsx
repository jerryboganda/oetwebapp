import React, {
  useState,
  ForwardRefExoticComponent,
  RefAttributes,
} from "react";
import {
  Offcanvas,
  OffcanvasBody,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
  UncontrolledDropdown,
} from "reactstrap";
import { IconMessageCircle, IconSearch } from "@tabler/icons-react";
import { Gear, IconProps } from "phosphor-react";
import { searchData } from "@/Data/HeaderMenuData";
import { Search } from "iconoir-react";

let DOMPurify: any = null;
if (typeof window !== "undefined") {
  DOMPurify = require("dompurify");
}

type SearchItem = {
  bgColor: string;
  icon: ForwardRefExoticComponent<IconProps & RefAttributes<SVGSVGElement>>;
  title: string;
  id: string;
};

const HeaderSearchbar: React.FC = () => {
  const [searchTerm, setSearchTerm] = useState("");
  const [isOpen, setIsOpen] = useState(false);

  const filteredItems = searchData.filter((item) =>
    item.title.toLowerCase().includes(searchTerm.toLowerCase())
  );

  const highlightText = (text: string, highlight: string) => {
    if (!highlight) return text;
    const regex = new RegExp(`(${highlight})`, "gi");
    return text.replace(regex, `<span class="highlight-searchtext">$1</span>`);
  };

  return (
    <>
      <a
        color="link"
        className="d-block head-icon p-0 border-0 shadow-none"
        onClick={() => setIsOpen(true)}
      >
        <Search className="fs-6" />
      </a>

      <Offcanvas
        isOpen={isOpen}
        toggle={() => setIsOpen(false)}
        direction="end"
        className="header-searchbar-canvas"
      >
        <OffcanvasBody className="app-scroll p-0">
          <div className="header-searchbar-header">
            <div className="d-flex justify-content-between align-items-center w-100">
              <form
                className="app-form app-icon-form w-100"
                onSubmit={(e) => e.preventDefault()}
              >
                <div className="position-relative">
                  <input
                    type="search"
                    className="form-control search-filter"
                    placeholder="Search..."
                    aria-label="Search"
                    value={searchTerm}
                    onChange={(e) => setSearchTerm(e.target.value)}
                  />
                  <span className="text-dark position-absolute start-0 top-0 me-3">
                    <IconSearch size={20} />
                  </span>
                </div>
              </form>

              <UncontrolledDropdown className="ms-2 app-dropdown">
                <DropdownToggle
                  tag="button"
                  className="h-35 w-35 d-flex-center b-r-15 overflow-hidden bg-light-secondary search-list-avatar border-0"
                >
                  <Gear size={20} weight="duotone" className="f-s-20" />
                </DropdownToggle>
                <DropdownMenu className="mb-3 p-2">
                  <DropdownItem header>Search Settings</DropdownItem>
                  <DropdownItem className="d-flex justify-content-between align-items-center">
                    <span className="text-secondary f-s-14">
                      Safe Search Filtering
                    </span>
                    <div className="form-check form-switch">
                      <input
                        className="form-check-input form-check-primary"
                        type="checkbox"
                        id="searchSwitch"
                        defaultChecked
                      />
                    </div>
                  </DropdownItem>
                  <DropdownItem className="d-flex justify-content-between align-items-center">
                    <span className="text-secondary f-s-14">
                      Search Suggestions
                    </span>
                    <div className="form-check form-switch">
                      <input
                        className="form-check-input form-check-primary"
                        type="checkbox"
                        id="searchSwitch1"
                      />
                    </div>
                  </DropdownItem>
                  <DropdownItem className="d-flex justify-content-between align-items-center">
                    <span className="text-secondary f-s-14">
                      Search History
                    </span>
                    <span className="me-3 text-success">
                      <IconMessageCircle size={20} />
                    </span>
                  </DropdownItem>
                  <DropdownItem divider />
                  <DropdownItem className="d-flex justify-content-between align-items-center">
                    <span className="text-dark f-s-14">
                      Custom Search Preferences
                    </span>
                    <div className="form-check form-switch">
                      <input
                        className="form-check-input form-check-primary"
                        type="checkbox"
                        id="searchSwitch2"
                      />
                    </div>
                  </DropdownItem>
                </DropdownMenu>
              </UncontrolledDropdown>
            </div>
            <p className="mb-0 text-secondary f-s-15 mt-2">
              Recently Searched Data:
            </p>
          </div>
          <ul className="search-list">
            {filteredItems.map((item: SearchItem, index: number) => (
              <li className="search-list-item d-flex" key={index}>
                <div
                  className={`h-35 w-35 d-flex-center b-r-15 overflow-hidden ${item.bgColor} search-list-avatar`}
                >
                  <item.icon weight="duotone" className="f-s-20" />
                </div>
                <div className="search-list-content">
                  <h6
                    className="mb-0 text-dark"
                    dangerouslySetInnerHTML={{
                      __html:
                        typeof window !== "undefined" && DOMPurify
                          ? DOMPurify.sanitize(
                              highlightText(item.title, searchTerm)
                            )
                          : highlightText(item.title, searchTerm),
                    }}
                  ></h6>
                  <p className="f-s-13 mb-0 text-secondary">{item.id}</p>
                </div>
              </li>
            ))}
          </ul>
        </OffcanvasBody>
      </Offcanvas>
    </>
  );
};

export default HeaderSearchbar;
