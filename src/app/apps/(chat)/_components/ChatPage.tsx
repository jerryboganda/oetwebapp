"use client";
import React, { useState } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Container,
  Card,
  CardHeader,
  Row,
  Col,
  CardBody,
  Button,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
} from "reactstrap";
import ChatLeftData from "@/app/apps/(chat)/_components/ChatLeftData";
import ChatContainer from "@/app/apps/(chat)/_components/ChatContainer";
import {
  IconAlignJustified,
  IconBrandHipchat,
  IconPhoneCall,
  IconSettings,
  IconStack2,
} from "@tabler/icons-react";

const ChatPage: React.FC = () => {
  const [isSidebarOpen, setIsSidebarOpen] = useState(true);
  const [dropdownOpen, setDropdownOpen] = useState(false);

  const handleToggleClick = () => setIsSidebarOpen(!isSidebarOpen);
  const handleCloseClick = () => setIsSidebarOpen(false);

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Chat"
          title="Apps"
          path={["Chat"]}
          Icon={IconStack2}
        />
        <Row className="row position-relative chat-container-box">
          {/* Sidebar / Left Chat Panel */}
          <Col lg="4" xxl="3" className="box-col-5">
            <div className="chat-div">
              <Card>
                <CardHeader>
                  <div className="d-flex align-items-center">
                    <span className="chatdp h-45 w-45 d-flex-center b-r-50 position-relative bg-danger">
                      <img
                        src="/images/avatar/9.png"
                        alt="User Avatar"
                        className="img-fluid b-r-50"
                      />
                      <span className="position-absolute top-0 end-0 p-1 bg-success border border-light rounded-circle" />
                    </span>

                    <div className="flex-grow-1 ps-2">
                      <h6 className="mb-0">Ninfa Monaldo</h6>
                      <p className="text-secondary mb-0 f-s-12">
                        Web Developer
                      </p>
                    </div>

                    <div>
                      <div className="btn-group dropdown-icon-none">
                        <Dropdown
                          isOpen={dropdownOpen}
                          toggle={() => setDropdownOpen(!dropdownOpen)}
                        >
                          <DropdownToggle tag="span" className="cursor-pointer">
                            <IconSettings size={18} />
                          </DropdownToggle>
                          <DropdownMenu>
                            <DropdownItem>
                              <IconBrandHipchat size={18} />{" "}
                              <span className="f-s-13">Chat Settings</span>
                            </DropdownItem>
                            <DropdownItem>
                              <IconPhoneCall size={18} />{" "}
                              <span className="f-s-13">Contact Settings</span>
                            </DropdownItem>
                            <DropdownItem>
                              <IconSettings size={18} />{" "}
                              <span className="f-s-13">Settings</span>
                            </DropdownItem>
                          </DropdownMenu>
                        </Dropdown>
                      </div>
                    </div>

                    {/* Close Button */}
                    <div className="close-togglebtn">
                      <button
                        onClick={handleCloseClick}
                        className="ms-2 close-toggle btn btn-link"
                      >
                        <IconAlignJustified size={18} />
                      </button>
                    </div>
                  </div>
                </CardHeader>
                <CardBody>
                  <ChatLeftData />
                </CardBody>
              </Card>
            </div>
          </Col>

          {/* Chat Content Panel */}
          <Col lg="8" xxl="9" className="box-col-7">
            <ChatContainer />

            <div className="d-block d-lg-none position-absolute top-0 start-0 p-2 zindex-dropdown">
              <Button
                className="toggle-btn"
                color="link"
                onClick={handleToggleClick}
              >
                <IconAlignJustified size={18} />
              </Button>
            </div>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ChatPage;
