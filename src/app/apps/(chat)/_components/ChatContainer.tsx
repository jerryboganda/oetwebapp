import { chatmessages } from "@/Data/Apps/Chat/ChatData";
import React from "react";
import {
  Card,
  CardHeader,
  CardBody,
  CardFooter,
  Button,
  InputGroup,
  Input,
  Modal,
  ModalBody,
  Col,
  Tooltip,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
} from "reactstrap";
import Link from "next/link";
import {
  IconBrandHipchat,
  IconCameraPlus,
  IconChecks,
  IconDotsVertical,
  IconMicrophone,
  IconMoodSmile,
  IconPaperclip,
  IconPhoneCall,
  IconSend,
  IconSettings,
  IconVideo,
} from "@tabler/icons-react";

interface TooltipState {
  emoji: boolean;
  microphone: boolean;
  camera: boolean;
  paperclip: boolean;
}

function ChatContainer() {
  const [callModal, setCallModal] = React.useState(false);
  const [videoModal, setVideoModal] = React.useState(false);

  const toggleCallModal = () => setCallModal(!callModal);
  const toggleVideoModal = () => setVideoModal(!videoModal);

  const [tooltipOpen, setTooltipOpen] = React.useState<TooltipState>({
    emoji: false,
    microphone: false,
    camera: false,
    paperclip: false,
  });
  const [mobileDropdown, setMobileDropdown] = React.useState(false);

  const toggleTooltip = (name: keyof TooltipState) => {
    setTooltipOpen((prevState) => ({
      ...prevState,
      [name]: !prevState[name],
    }));
  };

  return (
    <>
      <Card className="chat-container-content-box">
        <CardHeader>
          <div className="chat-header d-flex align-items-center ms-lg-0 ms-5">
            {/* Profile Section */}
            <Link href="/apps/profile">
              <span className="profileimg h-45 w-45 d-flex-center b-r-50 position-relative bg-light">
                <img
                  src="/images/avatar/14.png"
                  alt="avatar"
                  className="img-fluid b-r-50"
                />
                <span className="position-absolute top-0 end-0 p-1 bg-success border border-light rounded-circle"></span>
              </span>
            </Link>

            <div className="flex-grow-1 ps-2 pe-2">
              <h6 className="mb-0">Jerry Ladies</h6>
              <p className="text-muted f-s-12 text-success mb-0">Online</p>
            </div>

            {/* Call Button */}
            <button
              color="success"
              className="h-45 w-45 icon-btn b-r-22 me-sm-2 btn  btn-light-success"
              onClick={toggleCallModal}
            >
              <IconPhoneCall size={18} />
            </button>

            {/* Call Modal */}
            <Modal isOpen={callModal} toggle={toggleCallModal} centered>
              <ModalBody className="p-0">
                <div className="call">
                  <div className="call-div">
                    <img
                      src="/images/profile/32.jpg"
                      className="w-100 rounded"
                      alt=""
                    />
                    <div className="call-caption">
                      <h2 className="text-white">Jerry Ladies</h2>
                      <div className="d-flex justify-content-center">
                        <span
                          className="bg-success h-40 w-40 d-flex-center b-r-50 call-btn pointer-events-auto"
                          onClick={toggleCallModal}
                        >
                          <IconPhoneCall size={18} />
                        </span>
                        <span
                          className="bg-danger h-40 w-40 d-flex-center b-r-50 ms-4 call-btn pointer-events-auto"
                          onClick={toggleCallModal}
                        >
                          <IconPhoneCall size={18} />
                        </span>
                      </div>
                    </div>
                  </div>
                </div>
              </ModalBody>
            </Modal>

            {/* Video Button */}
            <button
              color="primary"
              className="h-45 w-45 icon-btn b-r-22 me-sm-2 btn btn-light-primary"
              onClick={toggleVideoModal}
            >
              <IconVideo size={18} />
            </button>

            {/* Video Modal */}
            <Modal isOpen={videoModal} toggle={toggleVideoModal} centered>
              <ModalBody className="p-0">
                <div className="call">
                  <div className="call-div pointer-events-auto">
                    <img
                      src="/images/profile/25.jpg"
                      className="w-100 rounded"
                      alt=""
                    />
                    <div className="call-caption">
                      <div className="d-flex justify-content-center align-items-center">
                        <span className="bg-white h-35 w-35 d-flex-center b-r-50 ms-4">
                          <IconMicrophone className="text-dark" />
                        </span>
                        <span
                          className="bg-danger h-45 w-45 d-flex-center b-r-50 ms-4 call-btn pointer-events-auto"
                          onClick={toggleVideoModal}
                        >
                          <IconPhoneCall size={18} />
                        </span>
                        <span className="bg-white h-35 w-35 d-flex-center b-r-50 ms-4">
                          <IconPhoneCall size={18} className="text-dark" />
                        </span>
                      </div>
                    </div>
                  </div>
                  <div className="video-div">
                    <img
                      src="/images/profile/31.jpg"
                      className="w-100 rounded"
                      alt=""
                    />
                  </div>
                </div>
              </ModalBody>
            </Modal>

            {/* Settings Dropdown */}
            <Dropdown
              isOpen={mobileDropdown}
              toggle={() => setMobileDropdown(!mobileDropdown)}
            >
              <DropdownToggle className="btn btn-light-secondary h-45 w-45 icon-btn b-r-22 me-sm-2">
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
        </CardHeader>
        <CardBody>
          <div className="chat-container">
            <div className="text-center">
              <span className="badge text-light-secondary">Today</span>
            </div>

            {chatmessages.map((chat) => (
              <Col key={chat.id} xs="12" className="position-relative">
                {/* Left aligned chat */}
                {chat.position === "left" ? (
                  <>
                    <div className="chatdp h-45 w-45 b-r-50 position-absolute start-0 bg-light">
                      <img
                        src={chat.avatar}
                        alt="Avatar"
                        className="img-fluid b-r-50"
                      />
                    </div>
                    <div className="chat-box">
                      <div>
                        <p className="chat-text">{chat.message}</p>
                        <p className="text-muted">
                          <IconChecks size={18} className="text-primary" />{" "}
                          {chat.time}
                        </p>
                      </div>
                    </div>
                  </>
                ) : (
                  /* Right aligned chat */
                  <>
                    <div className="chat-box-right">
                      <div>
                        <p className="chat-text">{chat.message}</p>
                        <p className="text-muted">
                          <IconChecks size={18} className="text-primary" />{" "}
                          {chat.time}
                        </p>
                      </div>
                    </div>
                    <div className="chatdp h-45 w-45 b-r-50 position-absolute end-0 top-0 bg-danger">
                      <img
                        src={chat.avatar}
                        alt="Avatar"
                        className="img-fluid b-r-50"
                      />
                    </div>
                  </>
                )}
              </Col>
            ))}
          </div>
        </CardBody>

        <CardFooter>
          <div className="chat-footer d-flex">
            {/* Input and Emoji Button */}
            <div className="app-form flex-grow-1">
              <InputGroup>
                <span
                  className="input-group-text bg-secondary ms-2 me-2 b-r-10"
                  id="emojiTooltip"
                  role="button"
                >
                  <a className="emoji-btn d-flex-center">
                    <IconMoodSmile size={18} className="text-white" />
                  </a>
                </span>
                <Tooltip
                  isOpen={tooltipOpen.emoji}
                  target="emojiTooltip"
                  toggle={() => toggleTooltip("emoji")}
                >
                  Emoji
                </Tooltip>
                <Input
                  type="text"
                  className="form-control b-r-6"
                  placeholder="Type a message"
                  aria-label="Recipient's username"
                />
                <Button color="primary" className="btn-sm ms-2 me-2 b-r-4">
                  <IconSend size={18} /> <span>Send</span>
                </Button>
              </InputGroup>
            </div>

            {/* Additional Buttons */}
            <div className="d-none d-sm-block">
              <span
                className="bg-secondary h-50 w-50 d-flex-center b-r-10 ms-1"
                id="microphoneTooltip"
                role="button"
              >
                <IconMicrophone size={18} />
              </span>
              <Tooltip
                isOpen={tooltipOpen.microphone}
                target="microphoneTooltip"
                toggle={() => toggleTooltip("microphone")}
              >
                Microphone
              </Tooltip>
            </div>
            <div className="d-none d-sm-block">
              <span
                className="bg-secondary h-50 w-50 d-flex-center b-r-10 ms-1"
                id="cameraTooltip"
                role="button"
              >
                <IconCameraPlus size={18} />
              </span>
              <Tooltip
                isOpen={tooltipOpen.camera}
                target="cameraTooltip"
                toggle={() => toggleTooltip("camera")}
              >
                Camera
              </Tooltip>
            </div>
            <div className="d-none d-sm-block">
              <span
                className="bg-secondary h-50 w-50 d-flex-center b-r-10 ms-1"
                id="paperclipTooltip"
                role="button"
              >
                <IconPaperclip size={18} />
              </span>
              <Tooltip
                isOpen={tooltipOpen.paperclip}
                target="paperclipTooltip"
                toggle={() => toggleTooltip("paperclip")}
              >
                Paperclip
              </Tooltip>
            </div>

            {/* Dropdown for Mobile */}
            <div className="d-sm-none">
              <Dropdown
                isOpen={mobileDropdown}
                toggle={() => setMobileDropdown(!mobileDropdown)}
              >
                <DropdownToggle
                  tag="span"
                  className="h-35 w-35 d-flex-center ms-1"
                  role="button"
                >
                  <IconDotsVertical size={18} />
                </DropdownToggle>
                <DropdownMenu>
                  <DropdownItem>
                    <IconMicrophone size={18} />{" "}
                    <span className="f-s-13">Microphone</span>
                  </DropdownItem>
                  <DropdownItem>
                    <IconCameraPlus size={18} />{" "}
                    <span className="f-s-13">Camera</span>
                  </DropdownItem>
                  <DropdownItem>
                    <IconPaperclip size={18} />{" "}
                    <span className="f-s-13">Paperclip</span>
                  </DropdownItem>
                </DropdownMenu>
              </Dropdown>
            </div>
          </div>
        </CardFooter>
      </Card>
    </>
  );
}

export default ChatContainer;
