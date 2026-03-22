import React, { useState } from "react";
import {
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  Tooltip,
} from "reactstrap";
import { chatGroups, privateContacts } from "@/Data/Apps/Chat/ChatData";
import { LockKeyOpen, UsersThree } from "phosphor-react";
import { IconChecks } from "@tabler/icons-react";

const ChatContent = () => {
  const [activeTab, setActiveTab] = useState("1"); // Default active tab
  const [tooltipOpen, setTooltipOpen] = useState<Record<string, boolean>>({});

  const toggleTab = (tab: string) => {
    if (activeTab !== tab) setActiveTab(tab);
  };
  return (
    <>
      <div className="tab-wrapper">
        <div className="mt-3">
          <Nav
            tabs
            className="tab-light-primary chat-status-tab border-0 justify-content-between mb-0 pb-0"
            role="tablist"
          >
            <NavItem>
              <NavLink
                className={activeTab === "1" ? "active" : ""}
                onClick={() => toggleTab("1")}
                role="tab"
              >
                <LockKeyOpen size={18} weight="duotone" className="me-2" />
                Private
              </NavLink>
            </NavItem>
            <NavItem>
              <NavLink
                className={activeTab === "2" ? "active" : ""}
                onClick={() => toggleTab("2")}
                role="tab"
              >
                <UsersThree size={18} weight="duotone" className="me-2" />
                Group
              </NavLink>
            </NavItem>
          </Nav>
          <TabContent activeTab={activeTab}>
            <TabPane tabId="1">
              <div className="tab-pane-content">
                <div className="chat-contact">
                  {privateContacts.map((contact) => (
                    <div className="chat-contactbox" key={contact.id}>
                      <div className="position-relative me-3">
                        <span
                          className={`h-45 w-45 d-flex-center b-r-50 position-relative ${contact.status}`}
                        >
                          <img
                            src={contact.avatar}
                            alt={contact.name}
                            className="img-fluid b-r-50"
                          />
                          <span className="position-absolute top-0 end-0 p-1 bg-success border border-light rounded-circle"></span>
                        </span>
                      </div>
                      <div className="flex-grow-1">
                        <p className="mb-0 f-w-500 text-dark text-truncate">
                          {contact.name}
                        </p>
                        <p className="text-secondary mb-0 f-s-12 mb-0 chat-message">
                          <IconChecks size={18} />
                          {contact.message}
                        </p>
                      </div>
                      <div>
                        <p className="f-s-12 chat-time">{contact.time}</p>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </TabPane>
            <TabPane tabId="2">
              <div className="tab-pane-content">
                <div className="chat-contact chat-group-list">
                  {chatGroups.map((group, index) => (
                    <div key={index} className="chat-contactbox">
                      <div className="position-absolute">
                        <ul className="avatar-group">
                          {group.imageSrc ? (
                            <li
                              className={`h-45 w-45 d-flex-center overflow-hidden b-r-50 bg-${group.avatarBg}`}
                            >
                              <img
                                src={group.imageSrc}
                                alt={group.groupName}
                                className="img-fluid"
                              />
                            </li>
                          ) : (
                            <li
                              className={`text-bg-${group.avatarBg} h-45 w-45 d-flex-center b-r-50`}
                            >
                              {group.avatars[0]}
                            </li>
                          )}
                          {group.avatars[1] && (
                            <>
                              <li
                                className="text-bg-secondary h-35 w-35 d-flex-center b-r-50"
                                id={`tooltip-${index}`}
                              >
                                {group.avatars[1]}
                              </li>
                              <Tooltip
                                isOpen={
                                  tooltipOpen[`tooltip-${index}`] || false
                                }
                                target={`tooltip-${index}`}
                                toggle={() =>
                                  setTooltipOpen((prev) => ({
                                    ...prev,
                                    [`tooltip-${index}`]:
                                      !prev[`tooltip-${index}`],
                                  }))
                                }
                              >
                                {group.avatars[1]}
                              </Tooltip>
                            </>
                          )}
                        </ul>
                      </div>
                      <div className="flex-grow-1 text-start mg-s-75">
                        <p className="mb-0 f-w-500 text-dark txt-ellipsis-1">
                          {group.groupName}
                        </p>
                        <p className="text-secondary f-s-12 chat-message">
                          {group.lastMessage}
                        </p>
                      </div>
                      <div>
                        <p className="f-s-12 chat-time">{group.time}</p>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </TabPane>
          </TabContent>
        </div>
      </div>
    </>
  );
};

export default ChatContent;
