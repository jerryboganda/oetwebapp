import { Card, CardBody, Col } from "reactstrap";

const Effortless = ({ show }: { show?: boolean }) => {
  return (
    <Col md="6" xxl="4" className={show ? "order-1-md" : ""}>
      <Card className="project-connect-card">
        <CardBody className="pb-0">
          <div className="text-center">
            <h5 className="mb-2 f-s-24">
              Get started{" "}
              <span className="text-primary f-w-700">Effortlessly.</span>
            </h5>
            <p className="f-s-14 text-dark pb-0 txt-ellipsis-2">
              Connect your team&#39;s tools and unlock a unified view of every
              project&#39;s progress, deadlines, and team contributions.
            </p>
          </div>
          <div className="connect-chat-box">
            <div className="avatar-connect-box">
              <img
                alt="logo"
                className="avatar-connect-logo"
                src="/images/dashboard/project/avatar.png"
              />
              <img
                alt="logo"
                className="dribbble-connect-logo"
                src="/images/dashboard/project/dribbble.png"
              />
            </div>
            <img alt="img" src="/images/dashboard/project/chat.png" />
            <img
              alt="logo"
              className="slack-logo"
              src="/images/dashboard/project/slack.png"
            />
          </div>
        </CardBody>
      </Card>
    </Col>
  );
};

export default Effortless;
